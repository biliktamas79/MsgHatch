using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Pipeline;
using MsgHatch.Domain.Routing;

namespace MsgHatch.Domain.DependencyInjection;

/// <summary>One channel registration of the message type <typeparamref name="TEvent"/>, with no envelope type (section 4.3).</summary>
/// <typeparam name="TEvent">The type of the domain event.</typeparam>
internal interface IChannelRegistration<TEvent> : IEnvelopeUpgrade
{
    ChannelId Channel { get; }

    /// <summary>The dispatch topic of the channel, made one time (D280).</summary>
    DispatchTopic Topic { get; }

    Type EnvelopeType { get; }

    int Version { get; }

    /// <summary>
    /// Composes the routes of this channel, and writes the outgoing message or the error of each route into
    /// <paramref name="outcomes"/> at the position of the route (D280). <paramref name="positions"/> holds the position of
    /// each route, or is empty when the position of each route is its index. It gives the error of the composer for all
    /// routes, or null.
    /// </summary>
    ValueTask<Error?> ComposeAsync(
        IServiceProvider services, TenantKey key, ReceivedMessage<TEvent> received, ReadOnlyMemory<MessageRoute> routes,
        ReadOnlyMemory<int> positions, ErrorOr<OutgoingMessage>[] outcomes, OutgoingDefaults defaults, CancellationToken ct);
}
