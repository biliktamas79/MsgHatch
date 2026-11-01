using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Pipeline;
using MsgHatch.Domain.Routing;

namespace MsgHatch.Domain.DependencyInjection;

/// <summary>One channel registration of the message type <typeparamref name="TEvent"/>, with no envelope type (section 4.3).</summary>
/// <typeparam name="TEvent">The type of the domain event.</typeparam>
internal interface IChannelRegistration<TEvent>
{
    ChannelId Channel { get; }

    string ContentType { get; }

    int Version { get; }

    ValueTask<ErrorOr<IReadOnlyList<ErrorOr<OutgoingMessage>>>> ComposeAsync(
        IServiceProvider services, TenantKey key, ReceivedMessage<TEvent> received, ReadOnlyMemory<MessageRoute> routes,
        OutgoingDefaults defaults, CancellationToken ct);
}
