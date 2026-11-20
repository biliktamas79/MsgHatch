using System.Net;
using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Api.Client.DependencyInjection;
using MsgHatch.Api.Client.Tests.Unit.TestTypes;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Management;
using MsgHatch.Domain.Tests.Unit.TestTypes;

namespace MsgHatch.Api.Client.Tests.Unit;

/// <summary>
/// The requests and the failures of the HTTP delivery queries and commands on a stub handler (D233, D292, D294): the tenant of
/// a key as <c>tenantId</c>, no <c>tenantId</c> for the global commands, the rules before a call, the error of a network
/// failure and of a rejected token, the text of a content, and a response that the contract does not allow.
/// </summary>
public sealed class DeliveryApiClientRequestsUnitTests
{
    private static readonly Guid _id = new("7d2e4b1a-5c3f-5a9e-8b10-4f6a2d9c0e31");
    private static readonly TenantKey _t1 = TenantKey.For(new TenantId("t1"));
    private static readonly DateTime _time = TestHost.Start.UtcDateTime;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<HttpStatusCode> RejectedRows => [HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden];

    public static TheoryData<string> RuleRows =>
    [
        "traffic created from at created to", "inbox limit 0", "outbox ids 1001", "inbox retry with no filter", "outbox cancel with no filter",
        "pause note 501", "pause of a tenant", "resume of a tenant", "delete test data of a tenant", "purge max count 0", "purge of a tenant",
    ];

    public static TheoryData<string, string> NotAllowedPageRows => new()
    {
        { "a next cursor that does not parse", """{"items":[],"next":"2030-01-01_x"}""" },
        { "no items", """{"items":null,"next":null}""" },
        { "a null item", """{"items":[null],"next":null}""" },
        { "the JSON null", "null" },
    };

    [Fact]
    public async Task QueriesAndBulkActions_KeyOfATenant_SendTheTenantAsTenantId()
    {
        // Arrange
        var handler = new StubHandler(HttpStatusCode.OK, "{}");
        await using var provider = AProvider(handler);
        await using var scope = provider.CreateAsyncScope();
        var queries = scope.ServiceProvider.GetRequiredKeyedService<IDeliveryQueries>(_t1);
        var commands = scope.ServiceProvider.GetRequiredKeyedService<IDeliveryCommands>(_t1);
        var failed = new OutboxQuery(Status: DeliveryStatus.Failed);

        // Act
        await queries.GetChannelsAsync(Ct);
        await queries.GetTrafficAsync(new TrafficQuery(), Ct);
        await queries.GetReceivedMessagesAsync(new InboxQuery(), Ct);
        await queries.GetOutgoingMessagesAsync(new OutboxQuery(), Ct);
        await queries.GetOutgoingMessageAsync(_id, Ct);
        await queries.GetReceivedContentAsync(_id, Ct);
        await queries.GetOutgoingContentAsync(_id, Ct);
        await commands.RetryReceivedAsync(new InboxQuery(Status: DeliveryStatus.Failed), Ct);
        await commands.CancelReceivedAsync(new InboxQuery(Status: DeliveryStatus.Pending), Ct);
        await commands.RetryOutgoingAsync(failed, Ct);
        await commands.CancelOutgoingAsync(failed, Ct);

        // Assert
        Assert.Equal(
            [
                "/delivery/channels?tenantId=t1",
                "/delivery/traffic?includeTest=false&tenantId=t1",
                "/delivery/inbox?includeTest=false&limit=50&tenantId=t1",
                "/delivery/outbox?includeTest=false&limit=50&tenantId=t1",
                $"/delivery/outbox/{_id}?tenantId=t1",
                $"/delivery/inbox/{_id}/content?tenantId=t1",
                $"/delivery/outbox/{_id}/content?tenantId=t1",
                "/delivery/inbox/retry?tenantId=t1",
                "/delivery/inbox/cancel?tenantId=t1",
                "/delivery/outbox/retry?tenantId=t1",
                "/delivery/outbox/cancel?tenantId=t1",
            ],
            handler.Uris.Select(static uri => uri.PathAndQuery));
    }

    [Fact]
    public async Task Queries_KeyOfNoTenant_SendNoTenantId()
    {
        // Arrange
        var handler = new StubHandler(HttpStatusCode.OK, "[]");
        await using var provider = AProvider(handler);
        await using var scope = provider.CreateAsyncScope();

        // Act
        await scope.ServiceProvider.GetRequiredService<IDeliveryQueries>().GetChannelsAsync(Ct);

        // Assert
        Assert.Equal(["/delivery/channels"], handler.Uris.Select(static uri => uri.PathAndQuery));
    }

    [Fact]
    public async Task GlobalCommands_KeyOfNoTenant_SendNoTenantId()
    {
        // Arrange
        var handler = new StubHandler(HttpStatusCode.OK, """{"inboxMessages":0,"outboxMessages":0}""");
        await using var provider = AProvider(handler);
        await using var scope = provider.CreateAsyncScope();
        var commands = scope.ServiceProvider.GetRequiredService<IDeliveryCommands>();

        // Act
        await commands.PauseChannelAsync(TestChannel.Id, "note", Ct);
        await commands.ResumeChannelAsync(TestChannel.Id, Ct);
        await commands.DeleteTestDataAsync(Ct);
        await commands.PurgeAsync(_time, 10, Ct);

        // Assert
        Assert.Equal(
            ["/delivery/channels/Test/pause", "/delivery/channels/Test/resume", "/delivery/test-data", "/delivery/purge"],
            handler.Uris.Select(static uri => uri.PathAndQuery));
    }

    [Theory]
    [MemberData(nameof(RuleRows))]
    public async Task Request_BreaksARule_GivesValidationFailedAndSendsNoRequest(string row)
    {
        // Arrange
        var handler = new StubHandler(HttpStatusCode.OK, "{}");
        await using var provider = AProvider(handler);
        await using var scope = provider.CreateAsyncScope();
        var (queries, commands) = (scope.ServiceProvider.GetRequiredService<IDeliveryQueries>(), scope.ServiceProvider.GetRequiredService<IDeliveryCommands>());
        var tenant = scope.ServiceProvider.GetRequiredKeyedService<IDeliveryCommands>(_t1);

        // Act
        var error = row switch
        {
            "traffic created from at created to" => (await queries.GetTrafficAsync(new TrafficQuery(_time, _time), Ct)).Error,
            "inbox limit 0" => (await queries.GetReceivedMessagesAsync(new InboxQuery(Limit: 0), Ct)).Error,
            "outbox ids 1001" => (await queries.GetOutgoingMessagesAsync(new OutboxQuery(Ids: DeliveryTestServices.Ids(1001)), Ct)).Error,
            "inbox retry with no filter" => (await commands.RetryReceivedAsync(new InboxQuery(IncludeTest: true), Ct)).Error,
            "outbox cancel with no filter" => (await commands.CancelOutgoingAsync(new OutboxQuery(Limit: 10), Ct)).Error,
            "pause note 501" => await commands.PauseChannelAsync(TestChannel.Id, new string('n', 501), Ct),
            "pause of a tenant" => await tenant.PauseChannelAsync(TestChannel.Id, note: null, Ct),
            "resume of a tenant" => await tenant.ResumeChannelAsync(TestChannel.Id, Ct),
            "delete test data of a tenant" => (await tenant.DeleteTestDataAsync(Ct)).Error,
            "purge max count 0" => (await commands.PurgeAsync(_time, 0, Ct)).Error,
            _ => (await tenant.PurgeAsync(_time, 10, Ct)).Error,
        };

        // Assert
        Assert.Equal((row, Errors.ValidationFailed.Code, 0), (row, error?.Type.Code, handler.Uris.Count));
    }

    [Fact]
    public async Task QueriesAndCommands_NetworkError_GiveUnavailableWithTheException()
    {
        // Arrange
        var exception = new HttpRequestException(HttpRequestError.ConnectionError);
        await using var provider = AProvider(new StubHandler(HttpStatusCode.OK, "{}", exception));
        await using var scope = provider.CreateAsyncScope();

        // Act
        var channels = await scope.ServiceProvider.GetRequiredService<IDeliveryQueries>().GetChannelsAsync(Ct);
        var resume = await scope.ServiceProvider.GetRequiredService<IDeliveryCommands>().ResumeChannelAsync(TestChannel.Id, Ct);

        // Assert
        Assert.Equal((Errors.Unavailable, exception, Errors.Unavailable), (channels.Error?.Type, channels.Error?.Exception, resume?.Type));
    }

    [Theory]
    [MemberData(nameof(RejectedRows))]
    public async Task QueriesAndCommands_TokenThatTheHostRejects_GiveRejected(HttpStatusCode status)
    {
        // Arrange
        await using var provider = AProvider(new StubHandler(status, $$"""{"title":"{{status}}","status":{{(int)status}}}"""));
        await using var scope = provider.CreateAsyncScope();

        // Act
        var content = await scope.ServiceProvider.GetRequiredService<IDeliveryQueries>().GetOutgoingContentAsync(_id, Ct);
        var purge = await scope.ServiceProvider.GetRequiredService<IDeliveryCommands>().PurgeAsync(_time, 10, Ct);

        // Assert
        Assert.Equal((Errors.Rejected, Errors.Rejected), (content.Error?.Type, purge.Error?.Type));
    }

    [Fact]
    public async Task GetReceivedContentAsync_Body_GivesTheTextOfTheBodyAsItIs()
    {
        // Arrange
        const string json = """{ "schemaVersion": 1, "event": { "text": "Ősz-űr" } }""";
        await using var provider = AProvider(new StubHandler(HttpStatusCode.OK, json));
        await using var scope = provider.CreateAsyncScope();

        // Act
        var content = await scope.ServiceProvider.GetRequiredService<IDeliveryQueries>().GetReceivedContentAsync(_id, Ct);

        // Assert
        Assert.Equal(new MessageContent(_id, json), content.Value);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("\"text\"")]
    public async Task GetOutgoingContentAsync_BodyThatIsNotAJsonObject_GivesSerializationFailed(string json)
    {
        // Arrange
        await using var provider = AProvider(new StubHandler(HttpStatusCode.OK, json));
        await using var scope = provider.CreateAsyncScope();

        // Act
        var content = await scope.ServiceProvider.GetRequiredService<IDeliveryQueries>().GetOutgoingContentAsync(_id, Ct);

        // Assert
        Assert.Equal((json, Errors.SerializationFailed), (json, content.Error?.Type));
    }

    [Theory]
    [MemberData(nameof(NotAllowedPageRows))]
    public async Task GetOutgoingMessagesAsync_PageThatTheContractDoesNotAllow_GivesSerializationFailed(string row, string json)
    {
        // Arrange
        await using var provider = AProvider(new StubHandler(HttpStatusCode.OK, json));
        await using var scope = provider.CreateAsyncScope();

        // Act
        var page = await scope.ServiceProvider.GetRequiredService<IDeliveryQueries>().GetOutgoingMessagesAsync(new OutboxQuery(), Ct);

        // Assert
        Assert.Equal((row, Errors.SerializationFailed), (row, page.Error?.Type));
    }

    private static ServiceProvider AProvider(StubHandler handler) =>
        TestHost.CreateServices().AddMsgHatch(static o => o.Roles = MsgHatchRoles.None)
            .UseDeliveryApiClient(HttpRegistryHost.BaseAddress, http => http.ConfigurePrimaryHttpMessageHandler(() => handler)).Services.Build();
}
