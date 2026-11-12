using MsgHatch.Domain.Persistence;
using MsgHatch.Subscriptions.Management;

namespace MsgHatch.Subscriptions.AspNetCore;

/// <summary>One message type in the body of <c>GET {prefix}/message-types</c> (D81).</summary>
/// <param name="MessageType">The message type.</param>
/// <param name="Channels">The registered channels of the message type.</param>
/// <param name="AppliedSubscriptions">For each channel, the subscription or the opt-out that applies, each with its own level.</param>
public sealed record MessageTypeResponse(
    string MessageType, IReadOnlyList<string> Channels, IReadOnlyList<SubscriptionResponse> AppliedSubscriptions)
{
    internal static MessageTypeResponse From(MessageTypeState state, IContentTypeRegistry registry) =>
        new(
            state.Type.Name,
            [.. state.Channels.Select(static channel => channel.Name)],
            [.. state.AppliedSubscriptions.Select(subscription => SubscriptionResponse.From(subscription, registry))]);
}
