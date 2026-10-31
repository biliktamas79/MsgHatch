using MsgHatch.Domain.Identity;

namespace MsgHatch.Domain.DependencyInjection;

/// <summary>One channel registration of the message type <typeparamref name="TEvent"/>, with no envelope type (section 4.3).</summary>
/// <typeparam name="TEvent">The type of the domain event.</typeparam>
internal interface IChannelRegistration<TEvent>
{
    ChannelId Channel { get; }

    string ContentType { get; }

    int Version { get; }
}
