using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Subscriptions.Api.Client.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Email;
using MsgHatch.SignalR;
using MsgHatch.Subscriptions.Api.Client.Tests.Unit.TestTypes;
using MsgHatch.Subscriptions.Management;
using MsgHatch.Subscriptions.Tests.Unit.TestTypes;
using MsgHatch.Subscriptions.Verification;
using static MsgHatch.Subscriptions.Tests.Unit.Contracts.SubscriptionCommandsTestRows;

namespace MsgHatch.Subscriptions.Api.Client.Tests.Unit;

/// <summary>
/// The parity of the HTTP client and the in-process implementation (D233, D234): the same failing input on the same
/// scenario gives an error equal to the error of the in-process implementation with no exception, also with the same message.
/// </summary>
public sealed class SubscriptionApiClientParityUnitTests
{
    private static readonly Dictionary<string, Func<IServiceProvider, Task<Error?>>> _operations = new()
    {
        ["a draft that breaks a rule"] = static async services =>
            (await Commands(services).CreateAsync(Draft(User("1"), "A", EmailChannel.Id, new EmailChannelSettings(), SubscriptionKind.OptOut), Ct)).Error,
        ["a message type that is not registered"] = static async services =>
            (await Commands(services).CreateAsync(Draft(User("1"), "Z", EmailChannel.Id, new EmailChannelSettings()), Ct)).Error,
        ["the settings of another channel"] = static async services =>
            (await Commands(services).CreateAsync(Draft(User("1"), "C", EmailChannel.Id, new SignalRChannelSettings()), Ct)).Error,
        ["a language that is not a known culture"] = static async services =>
            (await Commands(services).CreateAsync(Draft(User("1"), "A", EmailChannel.Id, new EmailChannelSettings("xx-invalid")), Ct)).Error,
        ["a named user that the directory does not know"] = static async services =>
            (await Commands(services).CreateAsync(Draft(AcmeOwner, "D", EmailChannel.Id, new EmailChannelSettings(), recipients: NamedUsers("99")), Ct)).Error,
        ["a draft that conflicts"] = static async services =>
            (await Commands(services).CreateAsync(Draft(User("1"), "B", EmailChannel.Id, new EmailChannelSettings("hu")), Ct)).Error,
        ["an update of an unknown id"] = static async services =>
            (await Commands(services).UpdateAsync(Scenario.LabelId(51), 1, Draft(User("1"), "B", EmailChannel.Id, new EmailChannelSettings()), Ct)).Error,
        ["an update with an old revision"] = static async services =>
            (await Commands(services).UpdateAsync(Scenario.S6.Id, 7, Draft(User("1"), "B", EmailChannel.Id, new EmailChannelSettings()), Ct)).Error,
        ["an update that changes the owner"] = static async services =>
            (await Commands(services).UpdateAsync(Scenario.S6.Id, 1, Draft(User("2"), "B", EmailChannel.Id, new EmailChannelSettings()), Ct)).Error,
        ["a read of an unknown id"] = static async services => (await Queries(services).GetSubscriptionAsync(Scenario.LabelId(50), Ct)).Error,
        ["the message types of an unknown user"] = static async services =>
            (await Queries(services).GetMessageTypesAsync(new MessageTypeQuery(User: new UserId("99")), Ct)).Error,
        ["a verification with an empty language"] = static async services =>
            (await services.GetRequiredService<IRoutingVerifier>().VerifyAsync(new RoutingVerificationRequest([new UserId("1")], ["en", " "]), Ct)).Error,
    };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<string> OperationRows => [.. _operations.Keys];

    [Theory]
    [MemberData(nameof(OperationRows))]
    public async Task Operation_FailingInput_GivesTheErrorOfTheInProcessImplementationWithNoException(string row)
    {
        // Arrange
        await using var local = await InProcessManagementHost.StartAsync<ISubscriptionQueries>(ManagerHost.Services(TestHost.NewFakeTime()));
        await using var http = await HttpManagementHost.StartAsync<ISubscriptionQueries>(
            ManagerHost.Services(TestHost.NewFakeTime()),
            static (services, configureHttp) => HttpManagementHost.AddSameServices(services, ManagerHost.Services(TestHost.NewFakeTime()))
                .UseSubscriptionApiClient(HttpManagementHost.BaseAddress, configureHttp));
        await local.Server.SeedScenarioAsync();
        await http.Server.SeedScenarioAsync();

        // Act
        var localError = await _operations[row](local.Server);
        var clientError = await _operations[row](http.Client);

        // Assert
        Assert.Equal((row, true, localError! with { Exception = null }), (row, localError is not null, clientError));
    }

    private static ISubscriptionCommands Commands(IServiceProvider services) => services.GetRequiredService<ISubscriptionCommands>();

    private static ISubscriptionQueries Queries(IServiceProvider services) => services.GetRequiredService<ISubscriptionQueries>();
}
