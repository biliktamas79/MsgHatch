using System.Net;
using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Routing;
using MsgHatch.Email;
using MsgHatch.Subscriptions.Api.Client.DependencyInjection;
using MsgHatch.Subscriptions.Api.Client.Tests.Unit.TestTypes;
using MsgHatch.Subscriptions.Api.Contracts;
using MsgHatch.Subscriptions.Management;
using MsgHatch.Subscriptions.Tests.Unit.TestTypes;
using MsgHatch.Subscriptions.Verification;
using static MsgHatch.Subscriptions.Tests.Unit.Contracts.SubscriptionCommandsTestRows;

namespace MsgHatch.Subscriptions.Api.Client.Tests.Unit;

/// <summary>
/// The requests of the subscription API client: the rules before a request (D234, D248), the tenant of a key, the settings
/// of a channel with no settings type (D229), the errors of a wrong prefix and of a response that the contract does not
/// allow (D233), and the log of the client host.
/// </summary>
public sealed class SubscriptionApiClientRequestsUnitTests
{
    private static readonly Dictionary<string, Func<IServiceProvider, Task<Error?>>> _brokenRules = new()
    {
        ["a company owner with no company"] = static async services =>
            (await Commands(services).CreateAsync(Draft(SubscriptionOwner.ForCompany(default), "A", EmailChannel.Id, new EmailChannelSettings()), Ct)).Error,
        ["an opt-out with settings"] = static async services =>
            (await Commands(services).CreateAsync(Draft(User("1"), "A", EmailChannel.Id, new EmailChannelSettings(), SubscriptionKind.OptOut), Ct)).Error,
        ["a mandatory opt-out"] = static async services =>
            (await Commands(services).CreateAsync(Draft(SubscriptionOwner.Default, "A", EmailChannel.Id, null, SubscriptionKind.OptOut, mandatory: true), Ct)).Error,
        ["a channel subscription with no settings"] = static async services =>
            (await Commands(services).CreateAsync(Draft(User("1"), "A", EmailChannel.Id, null), Ct)).Error,
        ["a user subscription for the company"] = static async services =>
            (await Commands(services).CreateAsync(Draft(User("1"), "A", EmailChannel.Id, new EmailChannelSettings(), recipients: SubscriptionRecipients.Company), Ct)).Error,
        ["a mandatory user subscription"] = static async services =>
            (await Commands(services).CreateAsync(Draft(User("1"), "A", EmailChannel.Id, new EmailChannelSettings(), mandatory: true), Ct)).Error,
        ["101 named users"] = static async services =>
            (await Commands(services).CreateAsync(
                Draft(AcmeOwner, "D", EmailChannel.Id, new EmailChannelSettings(), recipients: NamedUsers([.. Enumerable.Range(1, 101).Select(static n => $"n{n}")])), Ct)).Error,
        ["an update with a duplicate named user"] = static async services =>
            (await Commands(services).UpdateAsync(
                Guid.NewGuid(), 1, Draft(AcmeOwner, "D", EmailChannel.Id, new EmailChannelSettings(), recipients: NamedUsers("2", "2")), Ct)).Error,
        ["a verification with an empty language"] = static async services =>
            (await services.GetRequiredService<IRoutingVerifier>().VerifyAsync(new RoutingVerificationRequest([new UserId("1")], [""]), Ct)).Error,
    };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<string> BrokenRuleRows => [.. _brokenRules.Keys];

    [Theory]
    [MemberData(nameof(BrokenRuleRows))]
    public async Task Operation_InputThatBreaksARule_GivesValidationFailedWithNoRequest(string row)
    {
        // Arrange
        var handler = new StubHandler(HttpStatusCode.OK, "{}");
        await using var provider = ClientOn(handler);
        await using var scope = provider.CreateAsyncScope();

        // Act
        var error = await _brokenRules[row](scope.ServiceProvider);

        // Assert
        Assert.Equal((row, Errors.ValidationFailed, 0), (row, error?.Type, handler.Uris.Count));
    }

    [Fact]
    public async Task Service_TenantKey_SendsTheTenantIdOnlyForATenant()
    {
        // Arrange
        var handler = new StubHandler(HttpStatusCode.OK, "[]");
        await using var provider = ClientOn(handler);
        await using var scope = provider.CreateAsyncScope();

        // Act
        await scope.ServiceProvider.GetRequiredKeyedService<ISubscriptionQueries>(TenantKey.None).GetChannelsAsync(Ct);
        await scope.ServiceProvider.GetRequiredKeyedService<ISubscriptionQueries>(TenantKey.For(new TenantId("t2"))).GetSubscriptionsAsync(
            new SubscriptionQuery(new MessageType("B"), Level: SubscriptionLevel.User), Ct);

        // Assert
        Assert.Equal(
            "/subscriptions/channels, /subscriptions?messageType=B&level=User&tenantId=t2",
            string.Join(", ", handler.Uris.Select(static uri => uri.PathAndQuery)));
    }

    [Theory]
    [InlineData("[null]", "a null item")]
    [InlineData("""[{"id":"1","name":"Email","features":"None","description":null,"enabled":true}]""", "a channel id that is not a name")]
    public async Task GetChannelsAsync_ResponseThatTheContractDoesNotAllow_GivesSerializationFailed(string json, string row)
    {
        // Arrange
        await using var provider = ClientOn(new StubHandler(HttpStatusCode.OK, json));
        await using var scope = provider.CreateAsyncScope();

        // Act
        var channels = await scope.ServiceProvider.GetRequiredService<ISubscriptionQueries>().GetChannelsAsync(Ct);

        // Assert
        Assert.Equal((row, Errors.SerializationFailed), (row, channels.Error?.Type));
    }

    [Fact]
    public async Task ChannelWithNoSettingsTypeInTheClientHost_ReadAndUpdate_GiveJsonChannelSettingsAndSendTheirJsonBackWithNoChange()
    {
        // Arrange
        var time = TestHost.NewFakeTime();
        await using var host = await HttpManagementHost.StartAsync<ISubscriptionQueries>(
            ManagerHost.Services(time, withPager: true),
            (services, configureHttp) => HttpManagementHost.AddSameServices(services, ManagerHost.Services(time)).UseSubscriptionApiClient(HttpManagementHost.BaseAddress, configureHttp));
        var stored = await host.Server.GetRequiredService<ISubscriptionCommands>().CreateAsync(Draft(User("1"), "P", ManagerHost.Pager, new PagerSettings(Priority: 3)), Ct);
        var queries = host.Service(TenantKey.None);

        // Act
        var channel = (await queries.GetChannelsAsync(Ct)).Value.Single(static channel => channel.Id == ManagerHost.Pager);
        var read = await queries.GetSubscriptionAsync(stored.Value.Id, Ct);
        var updated = await host.Client.GetRequiredService<ISubscriptionCommands>().UpdateAsync(
            read.Value.Id, read.Value.Revision, Draft(User("1"), "P", ManagerHost.Pager, read.Value.Settings), Ct);

        // Assert
        var json = Assert.IsType<JsonChannelSettings>(read.Value.Settings);
        var serverRead = await host.Server.GetRequiredService<ISubscriptionQueries>().GetSubscriptionAsync(stored.Value.Id, Ct);
        Assert.Equal(
            (typeof(ChannelSettings), 3, null, 2, (ChannelSettings)new PagerSettings(Priority: 3)),
            (channel.SettingsType, json.Json.GetProperty("priority").GetInt32(), updated.Error, serverRead.Value.Revision, serverRead.Value.Settings));
    }

    [Fact]
    public async Task Operation_PrefixThatTheHostDoesNotMap_GivesExceptionAndNotNotFound()
    {
        // Arrange
        await using var host = await HttpManagementHost.StartAsync<ISubscriptionQueries>(
            ManagerHost.Services(TestHost.NewFakeTime()),
            static (services, configureHttp) => services.AddClientHost().UseSubscriptionApiClient(
                static o => { o.BaseAddress = HttpManagementHost.BaseAddress; o.Prefix = "/wrong"; }, configureHttp));

        // Act
        var subscription = await host.Service(TenantKey.None).GetSubscriptionAsync(Scenario.S6.Id, Ct);

        // Assert
        Assert.Equal(Errors.Exception, subscription.Error?.Type);
    }

    [Fact]
    public async Task Operations_UserIdAndCompanyId_NoLogRecordOfTheClientHostHoldsThem()
    {
        // Arrange
        await using var host = await HttpManagementHost.StartAsync<ISubscriptionQueries>(ManagerHost.Services(TestHost.NewFakeTime()));
        var queries = host.Service(TenantKey.None);
        var query = new SubscriptionQuery(Company: new CompanyId("acme-secret"), User: new UserId("user-secret"));

        // Act
        await queries.GetSubscriptionsAsync(query, Ct);
        await queries.GetMessageTypesAsync(new MessageTypeQuery(Company: new CompanyId("acme-secret")), Ct);
        await queries.GetMessageTypesAsync(new MessageTypeQuery(User: new UserId("user-secret")), Ct);
        await ((HttpManagementHost<ISubscriptionQueries>)host).Client.GetRequiredService<ISubscriptionCommands>().CreateAsync(
            Draft(SubscriptionOwner.ForUser(new UserId("user-secret")), "A", EmailChannel.Id, new EmailChannelSettings()), Ct);

        // Assert
        var records = ((HttpManagementHost<ISubscriptionQueries>)host).Client.LogRecords();
        Assert.Equal(
            (true, true, false),
            (records.Any(static record => record.Category?.StartsWith("System.Net.Http.HttpClient.MsgHatch.SubscriptionApi", StringComparison.Ordinal) == true),
                records.Any(static record => record.Id.Id is >= 1120 and <= 1199),
                records.Any(static record => record.ContainsText("acme-secret") || record.ContainsText("user-secret"))));
    }

    private static ServiceProvider ClientOn(StubHandler handler)
    {
        var services = new ServiceCollection();
        HttpManagementHost.AddSameServices(services, ManagerHost.Services(TestHost.NewFakeTime()))
            .UseSubscriptionApiClient(HttpManagementHost.BaseAddress, http => http.ConfigurePrimaryHttpMessageHandler(() => handler));
        return services.Build();
    }

    private static ISubscriptionCommands Commands(IServiceProvider services) => services.GetRequiredService<ISubscriptionCommands>();
}
