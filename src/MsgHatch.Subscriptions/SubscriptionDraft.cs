using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Routing;

namespace MsgHatch.Subscriptions;

/// <summary>The input of a create or an update of a subscription (section 4.17).</summary>
/// <param name="Owner">The owner, with its level.</param>
/// <param name="Type">The message type.</param>
/// <param name="Kind">A channel subscription or an opt-out.</param>
/// <param name="Channel">The channel, or null for an opt-out for all channels.</param>
/// <param name="Settings">The settings of the channel, or null for an opt-out.</param>
/// <param name="Recipients">The recipients, or null for the scope users.</param>
/// <param name="Mandatory">True for a mandatory subscription (D88).</param>
public sealed record SubscriptionDraft(
    SubscriptionOwner Owner, MessageType Type, SubscriptionKind Kind, ChannelId? Channel, ChannelSettings? Settings,
    SubscriptionRecipients? Recipients = null, bool Mandatory = false);
