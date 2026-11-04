using MsgHatch.Domain.Identity;

namespace MsgHatch.Subscriptions.Management;

/// <summary>One message type with its registered channels and its applied subscriptions (D81).</summary>
/// <param name="Type">The message type.</param>
/// <param name="Channels">The registered channels of the message type.</param>
/// <param name="AppliedSubscriptions">For each channel, the subscription or the opt-out that applies, each with its own level.</param>
public sealed record MessageTypeState(
    MessageType Type, IReadOnlyList<ChannelId> Channels, IReadOnlyList<Subscription> AppliedSubscriptions);
