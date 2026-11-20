using System.Text.Json;
using MsgHatch.Api.Contracts;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Management;
using MsgHatch.Domain.Routing;

namespace MsgHatch.Api.Contracts.Tests.Unit;

/// <summary>The mappings of the response bodies of the delivery API to the domain types and back (D228, D291).</summary>
public sealed class DeliveryResponsesUnitTests
{
    private static readonly ChannelId _email = new("Email");
    private static readonly MessageType _type = new("OrderShipped");
    private static readonly DateTime _time = new(2030, 1, 1, 10, 0, 0, DateTimeKind.Utc);
    private static readonly Guid _id = new("7d2e4b1a-5c3f-5a9e-8b10-4f6a2d9c0e31");

    public static TheoryData<string, ChannelStatusResponse> InvalidChannelRows => new()
    {
        { "an id that is not a name", AChannelResponse() with { Id = "not a name" } },
        { "no id", AChannelResponse() with { Id = null! } },
        { "no name", AChannelResponse() with { Name = null! } },
    };

    public static TheoryData<string, TrafficReportResponse> InvalidReportRows => new()
    {
        { "a type that is not a name", AReport([ACount() with { Type = "1x" }]) },
        { "no type", AReport([ACount() with { Type = null! }]) },
        { "a channel that is not a name", AReport([ACount() with { Channel = "bad name" }]) },
        { "a null row", AReport([null!]) },
        { "no inbox", AReport([]) with { Inbox = null! } },
        { "no outbox", AReport([]) with { Outbox = null! } },
    };

    public static TheoryData<string, ReceivedMessageSummaryResponse> InvalidReceivedRows => new()
    {
        { "a tenant that is not valid", AReceivedResponse() with { Tenant = "" } },
        { "a type that is not a name", AReceivedResponse() with { Type = "not-a-name" } },
    };

    public static TheoryData<string, OutgoingMessageSummaryResponse> InvalidOutgoingRows => new()
    {
        { "a tenant that is not valid", AnOutgoingResponse() with { Tenant = "a\nb" } },
        { "a type that is not a name", AnOutgoingResponse() with { Type = "not-a-name" } },
        { "a channel that is not a name", AnOutgoingResponse() with { Channel = "bad name" } },
        { "a target with no id", AnOutgoingResponse() with { Target = new DeliveryTarget(DeliveryTargetKind.User, null!) } },
        { "no content type", AnOutgoingResponse() with { ContentType = null! } },
    };

    public static TheoryData<string, OutgoingMessageDetailResponse> InvalidDetailRows => new()
    {
        { "no summary", ADetailResponse() with { Summary = null! } },
        { "a summary that is not valid", ADetailResponse() with { Summary = AnOutgoingResponse() with { Channel = "bad name" } } },
        { "no tries", ADetailResponse() with { Tries = null! } },
        { "a null try", ADetailResponse() with { Tries = [null!] } },
    };

    [Fact]
    public void ToStatus_BodyOfAPausedChannel_GivesTheStateWithTheSettingsTypeChannelSettings()
    {
        // Arrange
        var down = new ChannelDown(_email, ChannelDownReason.Operator, _time, NextCheckAtUtc: null, ErrorCode: null, ErrorName: null, "ticket 42");
        var descriptor = new ChannelDescriptor<ChannelSettings>(_email, "Email", ChannelFeatures.Languages, "Sends emails", Enabled: false);
        var status = new ChannelStatus(descriptor, down, Pending: 3, PendingTest: 1, _time.AddMinutes(-5));

        // Act
        var mapped = ChannelStatusResponse.From(status).ToStatus();

        // Assert
        Assert.Equal((status, typeof(ChannelSettings)), (mapped.Value, mapped.Value.Descriptor.SettingsType));
    }

    [Theory]
    [MemberData(nameof(InvalidChannelRows))]
    public void ToStatus_BodyThatIsNotValid_GivesSerializationFailed(string row, ChannelStatusResponse body)
    {
        // Act
        var mapped = body.ToStatus();

        // Assert
        Assert.Equal((row, Errors.SerializationFailed), (row, mapped.Error?.Type));
    }

    [Fact]
    public void ToReport_BodyOfAReport_GivesTheCountsOfTheInboxAndOfTheOutbox()
    {
        // Arrange
        var report = new TrafficReport(
            [new MessageCount(_type, null, DeliveryStatus.Failed, Test: true, 3, null)],
            [new MessageCount(_type, _email, DeliveryStatus.Pending, Test: false, 5, _time)]);

        // Act
        var mapped = TrafficReportResponse.From(report).ToReport();

        // Assert
        Assert.Equal(report.Inbox, mapped.Value.Inbox);
        Assert.Equal(report.Outbox, mapped.Value.Outbox);
    }

    [Theory]
    [MemberData(nameof(InvalidReportRows))]
    public void ToReport_BodyThatIsNotValid_GivesSerializationFailed(string row, TrafficReportResponse body)
    {
        // Act
        var mapped = body.ToReport();

        // Assert
        Assert.Equal((row, Errors.SerializationFailed), (row, mapped.Error?.Type));
    }

    [Fact]
    public void ToSummary_BodyOfAReceivedMessageOfATenant_GivesTheSummary()
    {
        // Arrange
        var summary = new ReceivedMessageSummary(ADeliveryState(), new TenantId("north"), _type, ContentVersion: 2, MessageFlags.Test);

        // Act
        var mapped = ReceivedMessageSummaryResponse.From(summary).ToSummary();

        // Assert
        Assert.Equal(summary, mapped.Value);
    }

    [Theory]
    [MemberData(nameof(InvalidReceivedRows))]
    public void ToSummary_ReceivedBodyThatIsNotValid_GivesSerializationFailed(string row, ReceivedMessageSummaryResponse body)
    {
        // Act
        var mapped = body.ToSummary();

        // Assert
        Assert.Equal((row, Errors.SerializationFailed), (row, mapped.Error?.Type));
    }

    [Fact]
    public void ToSummary_BodyOfAnOutgoingMessageWithNoTenant_GivesTheSummary()
    {
        // Arrange
        var summary = AnOutgoingSummary();

        // Act
        var mapped = OutgoingMessageSummaryResponse.From(summary).ToSummary();

        // Assert
        Assert.Equal(summary, mapped.Value);
    }

    [Theory]
    [MemberData(nameof(InvalidOutgoingRows))]
    public void ToSummary_OutgoingBodyThatIsNotValid_GivesSerializationFailed(string row, OutgoingMessageSummaryResponse body)
    {
        // Act
        var mapped = body.ToSummary();

        // Assert
        Assert.Equal((row, Errors.SerializationFailed), (row, mapped.Error?.Type));
    }

    [Theory]
    [InlineData("""{"reason":"rule 7","level":3}""")]
    [InlineData(null)]
    public void ToDetail_BodyOfADetail_GivesTheSummaryTheTraceTextAndTheTries(string? trace)
    {
        // Arrange
        var summary = AnOutgoingSummary();
        IReadOnlyList<PublishTry> tries = [PublishTry.Cancel(_id, 1, _time)];

        // Act
        var body = OutgoingMessageDetailResponse.From(new OutgoingMessageDetail(summary, trace, tries));
        var mapped = body.ToDetail();

        // Assert
        Assert.Equal((summary, trace, trace is null ? JsonValueKind.Undefined : JsonValueKind.Object), (mapped.Value.Summary, mapped.Value.Trace, body.Trace?.ValueKind ?? JsonValueKind.Undefined));
        Assert.Equal(tries, mapped.Value.Tries);
    }

    [Theory]
    [MemberData(nameof(InvalidDetailRows))]
    public void ToDetail_BodyThatIsNotValid_GivesSerializationFailed(string row, OutgoingMessageDetailResponse body)
    {
        // Act
        var mapped = body.ToDetail();

        // Assert
        Assert.Equal((row, Errors.SerializationFailed), (row, mapped.Error?.Type));
    }

    private static ChannelStatusResponse AChannelResponse() =>
        new("Email", "Email", ChannelFeatures.None, Description: null, Enabled: true, Down: null, Pending: 0, PendingTest: 0, OldestDueAtUtc: null);

    private static MessageCountResponse ACount() => new("OrderShipped", "Email", DeliveryStatus.Pending, Test: false, 1, _time);

    private static TrafficReportResponse AReport(IReadOnlyList<MessageCountResponse> outbox) => new([], outbox);

    private static DeliveryState ADeliveryState() =>
        new(_id, DeliveryStatus.Failed, TryCount: 8, MaxTryCount: 8, _time, _time.AddMinutes(31), _time.AddMinutes(32), LeaseUntilUtc: null,
            ExpiresAtUtc: null, "Unavailable: Sending is paused for this account.");

    private static ReceivedMessageSummaryResponse AReceivedResponse() =>
        new(ADeliveryState(), Tenant: null, "OrderShipped", ContentVersion: 1, MessageFlags.None);

    private static OutgoingMessageSummary AnOutgoingSummary() =>
        new(ADeliveryState(), Guid.NewGuid(), Tenant: null, _type, _email, new DeliveryTarget(DeliveryTargetKind.User, "1"), "Email:OrderShippedEmail", 1, MessageFlags.None);

    private static OutgoingMessageSummaryResponse AnOutgoingResponse() => OutgoingMessageSummaryResponse.From(AnOutgoingSummary());

    private static OutgoingMessageDetailResponse ADetailResponse() => new(AnOutgoingResponse(), Trace: null, []);
}
