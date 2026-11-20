using System.Text.Json;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Management;
using MsgHatch.Domain.Routing;

namespace MsgHatch.Api.Contracts;

/// <summary>The state of one registered channel in the body of <c>GET {prefix}/channels</c> (section 4.19, D164, D291).</summary>
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
    /// <summary>Gives the body of the state of a channel (D228).</summary>
    /// <param name="status">The state.</param>
    /// <returns>The body.</returns>
    /// <exception cref="ArgumentNullException">The state is null.</exception>
    public static ChannelStatusResponse From(ChannelStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        return new ChannelStatusResponse(
            status.Descriptor.Id.Name, status.Descriptor.Name, status.Descriptor.Features, status.Descriptor.Description,
            status.Descriptor.Enabled, status.Down is { } down ? ChannelDownResponse.From(down) : null, status.Pending,
            status.PendingTest, status.OldestDueAtUtc);
    }

    /// <summary>
    /// Gives the state of the body. The body has no settings type, so the descriptor has the settings type
    /// <see cref="ChannelSettings"/>. An id that is not valid, or no name, gives <see cref="Errors.SerializationFailed"/> (D228).
    /// </summary>
    /// <returns>The state, or an error.</returns>
    public ErrorOr<ChannelStatus> ToStatus()
    {
        var id = WireValues.ChannelOf(Id);
        return id.IsError ? id.Error
            : Name is null ? WireValues.Invalid("A channel has a name.")
            : new ChannelStatus(
                new ChannelDescriptor<ChannelSettings>(id.Value, Name, Features, Description, Enabled), Down?.ToDown(id.Value), Pending,
                PendingTest, OldestDueAtUtc);
    }
}

/// <summary>One count row of <c>GET {prefix}/traffic</c> (D164, D291).</summary>
/// <param name="Type">The message type.</param>
/// <param name="Channel">The channel, or null for the inbox.</param>
/// <param name="Status">The status.</param>
/// <param name="Test">True for the test messages.</param>
/// <param name="Count">The number of messages.</param>
/// <param name="OldestDueAtUtc">The earliest next try time, only for the status <c>Pending</c>.</param>
public sealed record MessageCountResponse(string Type, string? Channel, DeliveryStatus Status, bool Test, int Count, DateTime? OldestDueAtUtc)
{
    /// <summary>Gives the body of a count (D228).</summary>
    /// <param name="count">The count.</param>
    /// <returns>The body.</returns>
    public static MessageCountResponse From(MessageCount count) =>
        new(count.Type.Name, count.Channel?.Name, count.Status, count.Test, count.Count, count.OldestDueAtUtc);

    /// <summary>Gives the count of the body. A name that is not valid gives <see cref="Errors.SerializationFailed"/> (D228).</summary>
    /// <returns>The count, or an error.</returns>
    public ErrorOr<MessageCount> ToCount()
    {
        var type = WireValues.MessageTypeOf(Type);
        var channel = Channel is null ? ErrorOr<ChannelId?>.ForValue(null) : WireValues.ChannelOf(Channel).ThenIfSuccess(static id => ErrorOr<ChannelId?>.ForValue(id));
        return (type.Error ?? channel.Error) is { } invalid
            ? invalid
            : new MessageCount(type.Value, channel.Value, Status, Test, Count, OldestDueAtUtc);
    }
}

/// <summary>The body of <c>GET {prefix}/traffic</c> (D291).</summary>
/// <param name="Inbox">The counts of the received messages.</param>
/// <param name="Outbox">The counts of the outgoing messages.</param>
public sealed record TrafficReportResponse(IReadOnlyList<MessageCountResponse> Inbox, IReadOnlyList<MessageCountResponse> Outbox)
{
    /// <summary>Gives the body of a traffic report (D228).</summary>
    /// <param name="report">The report.</param>
    /// <returns>The body.</returns>
    /// <exception cref="ArgumentNullException">The report is null.</exception>
    public static TrafficReportResponse From(TrafficReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return new TrafficReportResponse([.. report.Inbox.Select(MessageCountResponse.From)], [.. report.Outbox.Select(MessageCountResponse.From)]);
    }

    /// <summary>
    /// Gives the report of the body. No list, a null row, or a row that is not valid gives
    /// <see cref="Errors.SerializationFailed"/> (D228).
    /// </summary>
    /// <returns>The report, or an error.</returns>
    public ErrorOr<TrafficReport> ToReport()
    {
        var inbox = CountsOf(Inbox);
        var outbox = CountsOf(Outbox);
        return (inbox.Error ?? outbox.Error) is { } invalid ? invalid : new TrafficReport(inbox.Value, outbox.Value);
    }

    private static ErrorOr<IReadOnlyList<MessageCount>> CountsOf(IReadOnlyList<MessageCountResponse>? rows)
    {
        if (rows is null)
        {
            return WireValues.Invalid("A traffic report has the counts of the inbox and of the outbox.");
        }

        var counts = new MessageCount[rows.Count];
        for (var index = 0; index < counts.Length; index++)
        {
            var count = rows[index]?.ToCount() ?? WireValues.Invalid("A traffic report has no null count.");
            if (count.IsError)
            {
                return count.Error;
            }

            counts[index] = count.Value;
        }

        return counts;
    }
}

/// <summary>A received message with no content, in a page of <c>GET {prefix}/inbox</c> (D291).</summary>
/// <param name="Delivery">The delivery state.</param>
/// <param name="Tenant">The tenant, or null.</param>
/// <param name="Type">The message type.</param>
/// <param name="ContentVersion">The contract version of the domain event.</param>
/// <param name="Flags">The flags, as one text.</param>
public sealed record ReceivedMessageSummaryResponse(DeliveryState Delivery, string? Tenant, string Type, int ContentVersion, MessageFlags Flags)
{
    /// <summary>Gives the body of a summary (D228).</summary>
    /// <param name="summary">The summary.</param>
    /// <returns>The body.</returns>
    /// <exception cref="ArgumentNullException">The summary is null.</exception>
    public static ReceivedMessageSummaryResponse From(ReceivedMessageSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        return new ReceivedMessageSummaryResponse(summary.Delivery, summary.Tenant?.Value, summary.Type.Name, summary.ContentVersion, summary.Flags);
    }

    /// <summary>Gives the summary of the body. A tenant or a name that is not valid gives <see cref="Errors.SerializationFailed"/> (D228).</summary>
    /// <returns>The summary, or an error.</returns>
    public ErrorOr<ReceivedMessageSummary> ToSummary()
    {
        var tenant = WireValues.TenantOf(Tenant);
        var type = WireValues.MessageTypeOf(Type);
        return (tenant.Error ?? type.Error) is { } invalid
            ? invalid
            : new ReceivedMessageSummary(Delivery, tenant.Value, type.Value, ContentVersion, Flags);
    }
}

/// <summary>An outgoing message with no content, in a page of <c>GET {prefix}/outbox</c> (D291).</summary>
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
    /// <summary>Gives the body of a summary (D228).</summary>
    /// <param name="summary">The summary.</param>
    /// <returns>The body.</returns>
    /// <exception cref="ArgumentNullException">The summary is null.</exception>
    public static OutgoingMessageSummaryResponse From(OutgoingMessageSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        return new OutgoingMessageSummaryResponse(
            summary.Delivery, summary.SourceMessageId, summary.Tenant?.Value, summary.Type.Name, summary.Channel.Name, summary.Target,
            summary.ContentType, summary.ContentVersion, summary.Flags);
    }

    /// <summary>
    /// Gives the summary of the body. A tenant or a name that is not valid, a target with no id, or no content type gives
    /// <see cref="Errors.SerializationFailed"/> (D228).
    /// </summary>
    /// <returns>The summary, or an error.</returns>
    public ErrorOr<OutgoingMessageSummary> ToSummary()
    {
        var tenant = WireValues.TenantOf(Tenant);
        var type = WireValues.MessageTypeOf(Type);
        var channel = WireValues.ChannelOf(Channel);
        return (tenant.Error ?? type.Error ?? channel.Error) is { } invalid ? invalid
            : Target.Id is null ? WireValues.Invalid("A target has an id.")
            : ContentType is null ? WireValues.Invalid("An outgoing message has a content type.")
            : new OutgoingMessageSummary(Delivery, SourceMessageId, tenant.Value, type.Value, channel.Value, Target, ContentType, ContentVersion, Flags);
    }
}

/// <summary>One page of messages, newest first (D112, D291).</summary>
/// <typeparam name="T">The summary type.</typeparam>
/// <param name="Items">The messages.</param>
/// <param name="Next">The cursor text of the next page, <c>{CreatedAtUtc}_{Id}</c>, or null for the last page.</param>
public sealed record MessagePageResponse<T>(IReadOnlyList<T> Items, string? Next);

/// <summary>The body of <c>GET {prefix}/outbox/{id}</c>: the summary, the route trace, and the publish tries, with no envelope (D109, D291).</summary>
/// <param name="Summary">The summary.</param>
/// <param name="Trace">The route trace as the JSON of its registered trace type, or null.</param>
/// <param name="Tries">The publish tries, in the order of their numbers.</param>
public sealed record OutgoingMessageDetailResponse(OutgoingMessageSummaryResponse Summary, JsonElement? Trace, IReadOnlyList<PublishTry> Tries)
{
    /// <summary>Gives the body of a detail, with the JSON text of the trace as a JSON value (D228, D290).</summary>
    /// <param name="detail">The detail.</param>
    /// <returns>The body.</returns>
    /// <exception cref="ArgumentNullException">The detail is null.</exception>
    /// <exception cref="JsonException">The trace is not JSON text.</exception>
    public static OutgoingMessageDetailResponse From(OutgoingMessageDetail detail)
    {
        ArgumentNullException.ThrowIfNull(detail);
        return new OutgoingMessageDetailResponse(OutgoingMessageSummaryResponse.From(detail.Summary), JsonOf(detail.Trace), detail.Tries);
    }

    /// <summary>
    /// Gives the detail of the body, with the trace as its JSON text. No summary, a summary that is not valid, no list of tries,
    /// or a null try gives <see cref="Errors.SerializationFailed"/> (D228).
    /// </summary>
    /// <returns>The detail, or an error.</returns>
    public ErrorOr<OutgoingMessageDetail> ToDetail()
    {
        var summary = Summary?.ToSummary() ?? WireValues.Invalid("A detail has a summary.");
        return summary.IsError ? summary.Error
            : Tries is null || Tries.Contains(null!) ? WireValues.Invalid("A detail has a list of tries with no null try.")
            : new OutgoingMessageDetail(summary.Value, Trace?.GetRawText(), Tries);
    }

    private static JsonElement? JsonOf(string? trace)
    {
        if (trace is null)
        {
            return null;
        }

        using var document = JsonDocument.Parse(trace);
        return document.RootElement.Clone();
    }
}
