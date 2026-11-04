using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Routing;

namespace MsgHatch.Subscriptions;

/// <summary>
/// One stored subscription of one owner for one message type and one set of recipients (section 4.4): either one channel
/// with its settings, or an opt-out.
/// </summary>
/// <param name="Id">The id.</param>
/// <param name="Revision">The revision. It starts at 1 and increases with each update (D79).</param>
/// <param name="Owner">The owner, with its level.</param>
/// <param name="Type">The message type.</param>
/// <param name="Kind">A channel subscription or an opt-out.</param>
/// <param name="Channel">The channel, or null for an opt-out for all channels.</param>
/// <param name="Settings">The settings of the channel, of its settings type, or null for an opt-out.</param>
/// <param name="Recipients">The recipients.</param>
/// <param name="Mandatory">
/// True for a channel subscription for the scope users at the default or the company level that always applies (D88).
/// </param>
/// <param name="CreatedAtUtc">The time of the create.</param>
/// <param name="LastModifiedAtUtc">The time of the last change.</param>
public sealed record Subscription(
    Guid Id,
    int Revision,
    SubscriptionOwner Owner,
    MessageType Type,
    SubscriptionKind Kind,
    ChannelId? Channel,
    ChannelSettings? Settings,
    SubscriptionRecipients Recipients,
    bool Mandatory,
    DateTime CreatedAtUtc,
    DateTime LastModifiedAtUtc)
{
    /// <summary>
    /// True for a mandatory subscription and for a subscription for the company or for named users. No lower level
    /// overrides a fixed subscription (D89).
    /// </summary>
    public bool IsFixed => Mandatory || Recipients.Kind != RecipientKind.ScopeUsers;
}
