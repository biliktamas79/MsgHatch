using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Persistence;
using MsgHatch.Subscriptions.Management;

namespace MsgHatch.Subscriptions.Api.Contracts;

/// <summary>One message type in the body of <c>GET {prefix}/message-types</c> (D81).</summary>
/// <param name="MessageType">The message type.</param>
/// <param name="Channels">The registered channels of the message type.</param>
/// <param name="AppliedSubscriptions">For each channel, the subscription or the opt-out that applies, each with its own level.</param>
public sealed record MessageTypeResponse(
    string MessageType, IReadOnlyList<string> Channels, IReadOnlyList<SubscriptionResponse> AppliedSubscriptions)
{
    /// <summary>Gives the body of a message type state (D228).</summary>
    /// <param name="state">The state.</param>
    /// <param name="registry">The content type registry, which gives the JSON of the settings types.</param>
    /// <returns>The body.</returns>
    public static MessageTypeResponse From(MessageTypeState state, IContentTypeRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(registry);
        return new MessageTypeResponse(
            state.Type.Name,
            [.. state.Channels.Select(static channel => channel.Name)],
            [.. state.AppliedSubscriptions.Select(subscription => SubscriptionResponse.From(subscription, registry))]);
    }

    /// <summary>Gives the message type state of the body. A value that is not valid gives <see cref="Errors.SerializationFailed"/> (D228).</summary>
    /// <param name="registry">The content type registry, which gives the settings types of the channels.</param>
    /// <returns>The state, or an error.</returns>
    public ErrorOr<MessageTypeState> ToMessageTypeState(IContentTypeRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        var type = WireValues.MessageTypeOf(MessageType);
        if (type.IsError)
        {
            return type.Error;
        }

        var channels = new ChannelId[(Channels ?? []).Count];
        for (var index = 0; index < channels.Length; index++)
        {
            var channel = WireValues.ChannelOf(Channels![index]);
            if (channel.IsError)
            {
                return channel.Error;
            }

            channels[index] = channel.Value;
        }

        var applied = new Subscription[(AppliedSubscriptions ?? []).Count];
        for (var index = 0; index < applied.Length; index++)
        {
            var subscription = AppliedSubscriptions![index].ToSubscription(registry);
            if (subscription.IsError)
            {
                return subscription.Error;
            }

            applied[index] = subscription.Value;
        }

        return new MessageTypeState(type.Value, channels, applied);
    }
}
