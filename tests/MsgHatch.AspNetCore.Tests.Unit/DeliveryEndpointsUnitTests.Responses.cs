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
        var pause = new ChannelDown(TestChannel.Id, ChannelDownReason.Operator, _time, NextCheckAtUtc: null, ErrorCode: null, ErrorName: null, "ticket 42");
        _host.Queries.GetChannelsAsync(Ct).ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<IReadOnlyList<ChannelStatus>>.ForValue(
            [new ChannelStatus(TestChannel.Descriptor, pause, Pending: 2, PendingTest: 1, _time)])));

        // Act
        var text = await _host.Client.GetStringAsync(new Uri("/delivery/channels", UriKind.Relative), Ct);

        // Assert
        using var json = JsonDocument.Parse(text);
        var channel = json.RootElement[0];
        Assert.Equal(
            ("Test", "Operator", "ticket 42", 2, 1),
            (channel.GetProperty("id").GetString(), channel.GetProperty("down").GetProperty("reason").GetString(),
                channel.GetProperty("down").GetProperty("note").GetString(), channel.GetProperty("pending").GetInt32(),
                channel.GetProperty("pendingTest").GetInt32()));
    }

    [Fact]
    public async Task GetTraffic_Report_WritesTheRowsOfTheInboxAndOfTheOutbox()
    {
        // Arrange
        var type = new MessageType("OrderShipped");
        var report = new TrafficReport(
            [new MessageCount(type, null, DeliveryStatus.Failed, Test: true, 3, null)],
            [new MessageCount(type, TestChannel.Id, DeliveryStatus.Pending, Test: false, 5, _time)]);
        _host.Queries.GetTrafficAsync(default!, Ct).ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<TrafficReport>.ForValue(report)));

        // Act
        var text = await _host.Client.GetStringAsync(new Uri("/delivery/traffic", UriKind.Relative), Ct);

        // Assert
        Assert.Equal(
            """
            {"inbox":[{"type":"OrderShipped","channel":null,"status":"Failed","test":true,"count":3,"oldestDueAtUtc":null}],"outbox":[{"type":"OrderShipped","channel":"Test","status":"Pending","test":false,"count":5,"oldestDueAtUtc":"2030-01-01T10:00:00Z"}]}
            """,
            text);
    }

    [Fact]
    public async Task GetReceivedMessages_Page_WritesTheSummariesWithTheirTenant()
    {
        // Arrange
        var message = TestMessages.AReceivedMessage(new TestEvent("event text"), DeliveryHost.T2, flags: MessageFlags.Test);
        _host.T2Queries.GetReceivedMessagesAsync(default!, Ct).ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<MessagePage<ReceivedMessageSummary>>.ForValue(
            new MessagePage<ReceivedMessageSummary>([ReceivedMessageSummary.Of(message)], Next: null))));

        // Act
        var text = await _host.Client.GetStringAsync(new Uri("/delivery/inbox?tenantId=t2", UriKind.Relative), Ct);

        // Assert
        using var json = JsonDocument.Parse(text);
        var item = json.RootElement.GetProperty("items")[0];
        Assert.Equal(
            ("t2", "TestEvent", "Test", JsonValueKind.Null, false),
            (item.GetProperty("tenant").GetString(), item.GetProperty("type").GetString(), item.GetProperty("flags").GetString(),
                json.RootElement.GetProperty("next").ValueKind, text.Contains("event text", StringComparison.Ordinal)));
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
    }
}
