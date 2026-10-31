using MsgHatch.Domain.Identity;

namespace MsgHatch.Domain.DependencyInjection;

/// <summary>One <c>As&lt;Channel&gt;</c> registration of a message type (section 4.3).</summary>
/// <typeparam name="TEvent">The type of the domain event.</typeparam>
/// <typeparam name="TEnvelope">The type of the channel envelope.</typeparam>
internal sealed class ChannelRegistration<TEvent, TEnvelope>(
    ChannelId channel, string contentType, int version, IReadOnlyDictionary<int, Func<object, TEnvelope>> upgrades)
    : IChannelRegistration<TEvent>
    where TEnvelope : class
{
    public ChannelId Channel { get; } = channel;

    public string ContentType { get; } = contentType;

    public int Version { get; } = version;

    public IReadOnlyDictionary<int, Func<object, TEnvelope>> Upgrades { get; } = upgrades;
}
