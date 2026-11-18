using System.Text.Json;
using MsgHatch.Api.Contracts;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Management;
using MsgHatch.Domain.Routing;

namespace MsgHatch.AspNetCore;

/// <summary>The state of one registered channel in the body of <c>GET {prefix}/channels</c> (section 4.19, D164).</summary>
/// <param name="Id">The id of the channel, for example <c>Email</c>.</param>
/// <param name="Name">The display name.</param>
/// <param name="Features">The features, as one text (D155).</param>
/// <param name="Description">The optional description.</param>
/// <param name="Enabled">False when the pipeline skips each route on the channel (D118).</param>
/// <param name="Down">The down state, or null when the channel is up.</param>
/// <param name="Pending">The number of pending real messages in all tenants.</param>
/// <param name="PendingTest">The number of pending test messages in all tenants.</param>
/// <param name="OldestDueAtUtc">The earliest next try time of all pending messages, or null.</param>
public sealed record ChannelStatusResponse(
    string Id, string Name, ChannelFeatures Features, string? Description, bool Enabled, ChannelDownResponse? Down, int Pending,
    int PendingTest, DateTime? OldestDueAtUtc)
{
    internal static ChannelStatusResponse From(ChannelStatus status) =>
        new(
            status.Descriptor.Id.Name, status.Descriptor.Name, status.Descriptor.Features, status.Descriptor.Description,
            status.Descriptor.Enabled, status.Down is { } down ? ChannelDownResponse.From(down) : null, status.Pending,
            status.PendingTest, status.OldestDueAtUtc);
}

/// <summary>One count row of <c>GET {prefix}/traffic</c> (D164).</summary>
/// <param name="Type">The message type.</param>
/// <param name="Channel">The channel, or null for the inbox.</param>
/// <param name="Status">The status.</param>
/// <param name="Test">True for the test messages.</param>
/// <param name="Count">The number of messages.</param>
/// <param name="OldestDueAtUtc">The earliest next try time, only for the status <c>Pending</c>.</param>
public sealed record MessageCountResponse(string Type, string? Channel, DeliveryStatus Status, bool Test, int Count, DateTime? OldestDueAtUtc)
{
    internal static MessageCountResponse From(MessageCount count) =>
        new(count.Type.Name, count.Channel?.Name, count.Status, count.Test, count.Count, count.OldestDueAtUtc);
}

/// <summary>The body of <c>GET {prefix}/traffic</c>.</summary>
/// <param name="Inbox">The counts of the received messages.</param>
/// <param name="Outbox">The counts of the outgoing messages.</param>
public sealed record TrafficReportResponse(IReadOnlyList<MessageCountResponse> Inbox, IReadOnlyList<MessageCountResponse> Outbox)
{
    internal static TrafficReportResponse From(TrafficReport report) =>
        new([.. report.Inbox.Select(MessageCountResponse.From)], [.. report.Outbox.Select(MessageCountResponse.From)]);
}

/// <summary>A received message with no content, in a page of <c>GET {prefix}/inbox</c>.</summary>
/// <param name="Delivery">The delivery state.</param>
/// <param name="Tenant">The tenant, or null.</param>
/// <param name="Type">The message type.</param>
/// <param name="ContentVersion">The contract version of the domain event.</param>
/// <param name="Flags">The flags, as one text.</param>
public sealed record ReceivedMessageSummaryResponse(DeliveryState Delivery, string? Tenant, string Type, int ContentVersion, MessageFlags Flags)
{
    internal static ReceivedMessageSummaryResponse From(ReceivedMessageSummary summary) =>
        new(summary.Delivery, summary.Tenant?.Value, summary.Type.Name, summary.ContentVersion, summary.Flags);
}

/// <summary>An outgoing message with no content, in a page of <c>GET {prefix}/outbox</c>.</summary>
/// <param name="Delivery">The delivery state.</param>
/// <param name="SourceMessageId">The id of the event that gave the message.</param>
/// <param name="Tenant">The tenant, or null.</param>
/// <param name="Type">The message type of the event.</param>
/// <param name="Channel">The channel.</param>
/// <param name="Target">The delivery target.</param>
/// <param name="ContentType">The content type of the channel envelope.</param>
/// <param name="ContentVersion">The contract version of the channel envelope.</param>
/// <param name="Flags">The flags, as one text.</param>
public sealed record OutgoingMessageSummaryResponse(
    DeliveryState Delivery, Guid SourceMessageId, string? Tenant, string Type, string Channel, DeliveryTarget Target,
    string ContentType, int ContentVersion, MessageFlags Flags)
{
    internal static OutgoingMessageSummaryResponse From(OutgoingMessageSummary summary) =>
        new(
            summary.Delivery, summary.SourceMessageId, summary.Tenant?.Value, summary.Type.Name, summary.Channel.Name, summary.Target,
            summary.ContentType, summary.ContentVersion, summary.Flags);
}

/// <summary>One page of messages, newest first (D112).</summary>
/// <typeparam name="T">The summary type.</typeparam>
/// <param name="Items">The messages.</param>
/// <param name="Next">The cursor text of the next page, <c>{CreatedAtUtc}_{Id}</c>, or null for the last page.</param>
public sealed record MessagePageResponse<T>(IReadOnlyList<T> Items, string? Next);

/// <summary>The body of <c>GET {prefix}/outbox/{id}</c>: the summary, the route trace, and the publish tries, with no envelope (D109).</summary>
/// <param name="Summary">The summary.</param>
/// <param name="Trace">The route trace as the JSON of its registered trace type, or null.</param>
/// <param name="Tries">The publish tries, in the order of their numbers.</param>
public sealed record OutgoingMessageDetailResponse(OutgoingMessageSummaryResponse Summary, JsonElement? Trace, IReadOnlyList<PublishTry> Tries);
