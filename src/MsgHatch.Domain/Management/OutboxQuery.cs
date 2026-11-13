using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Routing;

namespace MsgHatch.Domain.Management;

/// <summary>
/// The filters of a query of the outgoing messages of one tenant (section 4.19). A null filter matches each value. A
/// message matches when it matches all given filters.
/// </summary>
/// <param name="Ids">The ids, or null for each id.</param>
/// <param name="Status">The status, or null for each status.</param>
/// <param name="Type">The message type, or null for each message type.</param>
/// <param name="Channel">The channel, or null for each channel.</param>
/// <param name="Target">The delivery target, or null for each target.</param>
/// <param name="SourceMessageId">The id of the event that gave the message, or null.</param>
/// <param name="CreatedFromUtc">The earliest creation time, included, or null.</param>
/// <param name="CreatedToUtc">The creation time that ends the range, not included, or null.</param>
/// <param name="DueBeforeUtc">Matches only the pending messages whose next try time is before it, or null.</param>
/// <param name="IncludeTest">False leaves out the messages with <see cref="MessageFlags.Test"/>.</param>
/// <param name="Limit">The maximum number of messages of a page, 1 to 500.</param>
/// <param name="After">The cursor of the last message of the previous page, or null for the first page.</param>
public sealed record OutboxQuery(
    IReadOnlyList<Guid>? Ids = null, DeliveryStatus? Status = null, MessageType? Type = null,
    ChannelId? Channel = null, DeliveryTarget? Target = null, Guid? SourceMessageId = null,
    DateTime? CreatedFromUtc = null, DateTime? CreatedToUtc = null, DateTime? DueBeforeUtc = null,
    bool IncludeTest = false, int Limit = 50, MessageCursor? After = null)
{
    /// <summary>Tells whether an outgoing message matches the filters, for a store that reads the messages into memory. It does not check the cursor.</summary>
    /// <param name="message">The message.</param>
    /// <returns>True when the message matches.</returns>
    public bool Matches(OutgoingMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return (Ids is null || Ids.Contains(message.Delivery.Id))
            && (Type is null || Type == message.Type)
            && (Channel is null || Channel == message.Channel)
            && (Target is null || Target == message.Target)
            && (SourceMessageId is null || SourceMessageId == message.SourceMessageId)
            && QueryFilters.Match(message.Delivery, message.Flags, Status, CreatedFromUtc, CreatedToUtc, DueBeforeUtc, IncludeTest);
    }
}
