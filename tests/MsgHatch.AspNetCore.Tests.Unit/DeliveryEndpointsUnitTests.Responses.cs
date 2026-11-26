using System.Text.Json;
using MsgHatch.AspNetCore.Tests.Unit.TestTypes;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Management;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using NSubstitute;

namespace MsgHatch.AspNetCore.Tests.Unit;

public sealed partial class DeliveryEndpointsUnitTests
{
    [Fact]
    public async Task GetChannels_PausedChannel_WritesItsDownStateAndItsCounts()
    {
        // Arrange
        const string note = "ticket 42";
        const int pending = 2;
        const int pendingTest = 1;
        var pause = new ChannelDown(TestChannel.Id, ChannelDownReason.Operator, _time, NextCheckAtUtc: null, ErrorCode: null, ErrorName: null, note);
        _host.Queries.GetChannelsAsync(Ct).ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<IReadOnlyList<ChannelStatus>>.ForValue(
            [new ChannelStatus(TestChannel.Descriptor, pause, pending, pendingTest, _time)])));

        // Act
        var text = await _host.Client.GetStringAsync(new Uri("/delivery/channels", UriKind.Relative), Ct);

        // Assert
        using var json = JsonDocument.Parse(text);
        var channel = json.RootElement[0];
        Assert.Equal(
            (TestChannel.Id.Name, "Operator", note, pending, pendingTest),
            (channel.GetProperty("id").GetString(), channel.GetProperty("down").GetProperty("reason").GetString(),
                channel.GetProperty("down").GetProperty("note").GetString(), channel.GetProperty("pending").GetInt32(),
                channel.GetProperty("pendingTest").GetInt32()));
        await _host.Queries.Received(1).GetChannelsAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetTraffic_Report_WritesTheRowsOfTheInboxAndOfTheOutbox()
    {
        // Arrange
        const int failedCount = 3;
        const int pendingCount = 5;
        var type = new MessageType("OrderShipped");
        var report = new TrafficReport(
            [new MessageCount(type, null, DeliveryStatus.Failed, Test: true, failedCount, null)],
            [new MessageCount(type, TestChannel.Id, DeliveryStatus.Pending, Test: false, pendingCount, _time)]);
        _host.Queries.GetTrafficAsync(default!, Ct).ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<TrafficReport>.ForValue(report)));

        // Act
        var text = await _host.Client.GetStringAsync(new Uri("/delivery/traffic", UriKind.Relative), Ct);

        // Assert
        Assert.Equal(
            $$"""
            {"inbox":[{"type":"{{type.Name}}","channel":null,"status":"Failed","test":true,"count":{{failedCount}},"oldestDueAtUtc":null}],"outbox":[{"type":"{{type.Name}}","channel":"{{TestChannel.Id.Name}}","status":"Pending","test":false,"count":{{pendingCount}},"oldestDueAtUtc":"2030-01-01T10:00:00Z"}]}
            """,
            text);
        await _host.Queries.Received(1).GetTrafficAsync(new TrafficQuery(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetReceivedMessages_Page_WritesTheSummariesWithTheirTenant()
    {
        // Arrange
        const string eventText = "event text";
        var message = TestMessages.AReceivedMessage(new TestEvent(eventText), DeliveryHost.T2, flags: MessageFlags.Test);
        _host.T2Queries.GetReceivedMessagesAsync(default!, Ct).ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<MessagePage<ReceivedMessageSummary>>.ForValue(
            new MessagePage<ReceivedMessageSummary>([ReceivedMessageSummary.Of(message)], Next: null))));

        // Act
        var text = await _host.Client.GetStringAsync(new Uri($"/delivery/inbox?tenantId={DeliveryHost.T2.Value}", UriKind.Relative), Ct);

        // Assert
        using var json = JsonDocument.Parse(text);
        var item = json.RootElement.GetProperty("items")[0];
        Assert.Equal(
            (DeliveryHost.T2.Value, message.Type.Name, "Test", JsonValueKind.Null, false),
            (item.GetProperty("tenant").GetString(), item.GetProperty("type").GetString(), item.GetProperty("flags").GetString(),
                json.RootElement.GetProperty("next").ValueKind, text.Contains(eventText, StringComparison.Ordinal)));
        await _host.T2Queries.Received(1).GetReceivedMessagesAsync(new InboxQuery(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetOutgoingMessage_NoTrace_WritesANullTrace()
    {
        // Arrange
        var message = TestMessages.AnOutgoingMessage(id: _id);
        _host.Queries.GetOutgoingMessageAsync(_id, Ct).ReturnsForAnyArgs(ValueTask.FromResult(
            ErrorOr<OutgoingMessageDetail>.ForValue(new OutgoingMessageDetail(OutgoingMessageSummary.Of(message), Trace: null, []))));

        // Act
        var text = await _host.Client.GetStringAsync(new Uri($"/delivery/outbox/{_id}", UriKind.Relative), Ct);

        // Assert
        using var json = JsonDocument.Parse(text);
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("trace").ValueKind);
        await _host.Queries.Received(1).GetOutgoingMessageAsync(_id, Arg.Any<CancellationToken>());
    }
}
