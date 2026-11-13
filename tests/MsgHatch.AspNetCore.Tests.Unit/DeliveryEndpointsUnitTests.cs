using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MsgHatch.AspNetCore.Tests.Unit.TestTypes;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Management;
using MsgHatch.Domain.Persistence;
using MsgHatch.Domain.Routing;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using NSubstitute;

namespace MsgHatch.AspNetCore.Tests.Unit;

/// <summary>The delivery endpoints (section 4.19) on a <c>TestServer</c> with an <see cref="IDeliveryManager"/> substitute.</summary>
public sealed partial class DeliveryEndpointsUnitTests : IAsyncLifetime
{
    private static readonly Guid _id = new("7d2e4b1a-5c3f-5a9e-8b10-4f6a2d9c0e31");
    private static readonly DateTime _time = new(2030, 1, 1, 10, 0, 0, DateTimeKind.Utc);
    private DeliveryHost _host = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _host = await DeliveryHost.StartAsync();

    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    public static TheoryData<string, string, string?, string> RouteRows => new()
    {
        { "GET", "/delivery/channels", null, nameof(IDeliveryManager.GetChannelsAsync) },
        { "POST", "/delivery/channels/Test/pause", "{}", nameof(IDeliveryManager.PauseChannelAsync) },
        { "POST", "/delivery/channels/Test/resume", null, nameof(IDeliveryManager.ResumeChannelAsync) },
        { "GET", "/delivery/traffic", null, nameof(IDeliveryManager.GetTrafficAsync) },
        { "GET", "/delivery/inbox", null, nameof(IDeliveryManager.GetReceivedMessagesAsync) },
        { "POST", "/delivery/inbox/retry", """{"status":"Failed"}""", nameof(IDeliveryManager.RetryReceivedAsync) },
        { "POST", "/delivery/inbox/cancel", """{"status":"Pending"}""", nameof(IDeliveryManager.CancelReceivedAsync) },
        { "GET", "/delivery/outbox", null, nameof(IDeliveryManager.GetOutgoingMessagesAsync) },
        { "GET", "/delivery/outbox/7d2e4b1a-5c3f-5a9e-8b10-4f6a2d9c0e31", null, nameof(IDeliveryManager.GetOutgoingMessageAsync) },
        { "POST", "/delivery/outbox/retry", """{"channel":"Test"}""", nameof(IDeliveryManager.RetryOutgoingAsync) },
        { "POST", "/delivery/outbox/cancel", """{"channel":"Test"}""", nameof(IDeliveryManager.CancelOutgoingAsync) },
        { "POST", "/delivery/purge", """{"finishedBefore":"2030-01-01T00:00:00Z","maxCount":10}""", nameof(IDeliveryManager.PurgeAsync) },
        { "GET", "/delivery/inbox/7d2e4b1a-5c3f-5a9e-8b10-4f6a2d9c0e31/content", null, nameof(IDeliveryManager.GetReceivedContentAsync) },
        { "GET", "/delivery/outbox/7d2e4b1a-5c3f-5a9e-8b10-4f6a2d9c0e31/content", null, nameof(IDeliveryManager.GetOutgoingContentAsync) },
    };

    [Theory]
    [MemberData(nameof(RouteRows))]
    public async Task Route_Request_CallsTheMatchingManagerMethodOneTime(string method, string path, string? body, string expected)
    {
        // Act
        await _host.Client.SendAsync(RequestOf(method, path, body), Ct);

        // Assert
        Assert.Equal([expected], _host.Manager.ReceivedCalls().Select(static call => call.GetMethodInfo().Name));
    }

    [Fact]
    public async Task GetOutgoingMessages_EachQueryParameter_GivesTheMatchingQuery()
    {
        // Arrange
        Guid[] ids = [Guid.NewGuid(), Guid.NewGuid()];
        var source = Guid.NewGuid();
        var after = new MessageCursor(_time.AddTicks(1234567), _id);
        var path = $"/delivery/outbox?ids={ids[0]}&ids={ids[1]}&status=Failed&messageType=OrderShipped&channel=Email&targetKind=User"
            + $"&targetId=user%201&sourceMessageId={source}&createdFrom=2030-01-01T00:00:00Z&createdTo=2030-01-02T00:00:00Z"
            + "&dueBefore=2030-01-01T09:00:00Z&includeTest=true&limit=10&after=2030-01-01T10:00:00.1234567Z_7d2e4b1a-5c3f-5a9e-8b10-4f6a2d9c0e31";
        var expected = new OutboxQuery(
            null, DeliveryStatus.Failed, new MessageType("OrderShipped"), new ChannelId("Email"), new DeliveryTarget(DeliveryTargetKind.User, "user 1"),
            source, _time.AddHours(-10), _time.AddHours(14), _time.AddHours(-1), IncludeTest: true, Limit: 10, after);

        // Act
        await _host.Client.GetAsync(new Uri(path, UriKind.Relative), Ct);

        // Assert
        await _host.Manager.Received(1).GetOutgoingMessagesAsync(
            Arg.Is<OutboxQuery>(query => SameQuery(query, expected, ids)), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetReceivedMessages_EachQueryParameter_GivesTheMatchingQuery()
    {
        // Arrange
        var id = Guid.NewGuid();
        var path = $"/delivery/inbox?ids={id}&status=Pending&messageType=OrderShipped&createdFrom=2030-01-01T00:00:00Z"
            + "&createdTo=2030-01-02T00:00:00Z&dueBefore=2030-01-01T09:00:00Z&includeTest=false&limit=500";
        var expected = new InboxQuery(
            null, DeliveryStatus.Pending, new MessageType("OrderShipped"), _time.AddHours(-10), _time.AddHours(14), _time.AddHours(-1),
            IncludeTest: false, Limit: 500);

        // Act
        await _host.Client.GetAsync(new Uri(path, UriKind.Relative), Ct);

        // Assert
        await _host.Manager.Received(1).GetReceivedMessagesAsync(
            Arg.Is<InboxQuery>(query => SameQuery(query, expected, new[] { id })), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetTraffic_QueryParameters_GivesTheMatchingQuery()
    {
        // Act
        await _host.Client.GetAsync(
            new Uri("/delivery/traffic?createdFrom=2030-01-01T00:00:00Z&createdTo=2030-01-02T00:00:00Z&status=Cancelled&includeTest=true", UriKind.Relative), Ct);

        // Assert
        await _host.Manager.Received(1).GetTrafficAsync(
            new TrafficQuery(_time.AddHours(-10), _time.AddHours(14), DeliveryStatus.Cancelled, IncludeTest: true), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Pause_Note_CallsThePauseWithTheChannelAndTheNote()
    {
        // Arrange
        _host.Manager.PauseChannelAsync(default, default, Ct).ReturnsForAnyArgs(ValueTask.FromResult<Error?>(null));

        // Act
        var response = await _host.Client.SendAsync(RequestOf("POST", "/delivery/channels/Email/pause", """{"note":"SES outage, ticket 42"}"""), Ct);

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await _host.Manager.Received(1).PauseChannelAsync(new ChannelId("Email"), "SES outage, ticket 42", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Purge_Body_CallsThePurgeAndGivesBothCounts()
    {
        // Arrange
        _host.Manager.PurgeAsync(default, default, Ct)
            .ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<DeletedMessages>.ForValue(new DeletedMessages(3, 4))));

        // Act
        var response = await _host.Client.SendAsync(
            RequestOf("POST", "/delivery/purge", """{"finishedBefore":"2030-01-01T10:00:00Z","maxCount":100}"""), Ct);

        // Assert
        await _host.Manager.Received(1).PurgeAsync(_time, 100, Arg.Any<CancellationToken>());
        Assert.Equal("""{"inboxMessages":3,"outboxMessages":4}""", await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task RetryOutgoing_Body_CallsTheRetryWithTheFiltersAndGivesTheCounts()
    {
        // Arrange
        _host.Manager.RetryOutgoingAsync(default!, Ct)
            .ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<DeliveryActionResult>.ForValue(new DeliveryActionResult(5, 4))));
        var body = """{"status":"Failed","channel":"Email","targetKind":"User","targetId":"1","createdFrom":"2030-01-01T00:00:00Z","includeTest":true}""";

        // Act
        var response = await _host.Client.SendAsync(RequestOf("POST", "/delivery/outbox/retry", body), Ct);

        // Assert
        await _host.Manager.Received(1).RetryOutgoingAsync(
            new OutboxQuery(
                Status: DeliveryStatus.Failed, Channel: new ChannelId("Email"), Target: new DeliveryTarget(DeliveryTargetKind.User, "1"),
                CreatedFromUtc: _time.AddHours(-10), IncludeTest: true),
            Arg.Any<CancellationToken>());
        Assert.Equal("""{"matched":5,"changed":4}""", await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task GetOutgoingMessages_Page_GivesItemsAndANextCursorThatReadsBackAsTheSameCursor()
    {
        // Arrange
        var cursor = new MessageCursor(_time.AddTicks(1), _id);
        var summary = OutgoingMessageSummary.Of(TestMessages.AnOutgoingMessage(id: _id, flags: MessageFlags.Test));
        _host.Manager.GetOutgoingMessagesAsync(default!, Ct).ReturnsForAnyArgs(ValueTask.FromResult(
            ErrorOr<MessagePage<OutgoingMessageSummary>>.ForValue(new MessagePage<OutgoingMessageSummary>([summary], cursor))));
        using var first = JsonDocument.Parse(await _host.Client.GetStringAsync(new Uri("/delivery/outbox?limit=1", UriKind.Relative), Ct));
        var next = first.RootElement.GetProperty("next").GetString();

        // Act
        await _host.Client.GetAsync(new Uri($"/delivery/outbox?limit=1&after={next}", UriKind.Relative), Ct);

        // Assert
        Assert.Equal(
            ("2030-01-01T10:00:00.0000001Z_7d2e4b1a-5c3f-5a9e-8b10-4f6a2d9c0e31", _id.ToString(), "Test", "User"),
            (next, ItemOf(first).GetProperty("delivery").GetProperty("id").GetString(), ItemOf(first).GetProperty("flags").GetString(),
                ItemOf(first).GetProperty("target").GetProperty("kind").GetString()));
        await _host.Manager.Received(1).GetOutgoingMessagesAsync(Arg.Is<OutboxQuery>(query => query.After == cursor), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetOutgoingMessage_Detail_WritesTheSummaryTheTraceAndTheTriesWithNoEnvelope()
    {
        // Arrange
        var message = TestMessages.AnOutgoingMessage(new TestEnvelope("client1@example.com"), id: _id, trace: new TestTrace("rule 7"));
        var detail = new OutgoingMessageDetail(OutgoingMessageSummary.Of(message), message.Trace, [PublishTry.Cancel(_id, 1, _time)]);
        _host.Manager.GetOutgoingMessageAsync(_id, Ct).ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<OutgoingMessageDetail>.ForValue(detail)));

        // Act
        var text = await _host.Client.GetStringAsync(new Uri($"/delivery/outbox/{_id}", UriKind.Relative), Ct);

        // Assert
        using var json = JsonDocument.Parse(text);
        Assert.Equal(
            ("Test", "rule 7", "Cancelled", false),
            (json.RootElement.GetProperty("summary").GetProperty("channel").GetString(),
                json.RootElement.GetProperty("trace").GetProperty("reason").GetString(),
                json.RootElement.GetProperty("tries")[0].GetProperty("outcome").GetString(),
                text.Contains("client1@example.com", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task GetOutgoingContent_TestMessage_WritesTheMessageWithItsEnvelopeWithMsgHatchJson()
    {
        // Arrange
        var message = TestMessages.AnOutgoingMessage(new TestEnvelope("client1@example.com"), id: _id, flags: MessageFlags.Test);
        _host.Manager.GetOutgoingContentAsync(_id, Ct).ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<OutgoingMessage>.ForValue(message)));
        var expected = Encoding.UTF8.GetString(_host.Services.GetRequiredService<MsgHatchJson>().Serialize(message).Value);

        // Act
        var response = await _host.Client.GetAsync(new Uri($"/delivery/outbox/{_id}/content", UriKind.Relative), Ct);

        // Assert
        Assert.Equal(
            (expected, "application/json"),
            (await response.Content.ReadAsStringAsync(Ct), response.Content.Headers.ContentType?.MediaType));
    }

    [Fact]
    public async Task GetReceivedContent_Message_WritesTheMessageWithItsEventWithMsgHatchJson()
    {
        // Arrange
        var message = TestMessages.AReceivedMessage(new TestEvent("Ősz-űr"), delivery: TestMessages.ADeliveryState(_id));
        _host.Manager.GetReceivedContentAsync(_id, Ct).ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<ReceivedMessage>.ForValue(message)));
        var expected = Encoding.UTF8.GetString(_host.Services.GetRequiredService<MsgHatchJson>().Serialize(message).Value);

        // Act
        var text = await _host.Client.GetStringAsync(new Uri($"/delivery/inbox/{_id}/content", UriKind.Relative), Ct);

        // Assert
        Assert.Equal(expected, text);
    }

    private static bool SameQuery(OutboxQuery query, OutboxQuery expected, Guid[] ids) =>
        query with { Ids = null } == expected && query.Ids!.SequenceEqual(ids);

    private static bool SameQuery(InboxQuery query, InboxQuery expected, Guid[] ids) =>
        query with { Ids = null } == expected && query.Ids!.SequenceEqual(ids);

    private static JsonElement ItemOf(JsonDocument page) => page.RootElement.GetProperty("items")[0];

    private static HttpRequestMessage RequestOf(string method, string path, string? body) =>
        new(new HttpMethod(method), new Uri(path, UriKind.Relative))
        {
            Content = body is null ? null : new StringContent(body, Encoding.UTF8, "application/json"),
        };
}
