using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Routing;

namespace MsgHatch.Domain.DependencyInjection;

/// <summary>The registration of the message type <typeparamref name="TEvent"/> with its channel registrations (section 4.3).</summary>
/// <typeparam name="TEvent">The type of the domain event.</typeparam>
internal sealed class MessageRegistration<TEvent>(MessageType type, int version) : IMessageHandler
{
    private readonly List<IChannelRegistration<TEvent>> _channels = [];
    private readonly Dictionary<int, Func<object, TEvent>> _upgrades = [];
    private Type? _closedOpenRouterType;

    public MessageType Type { get; } = type;

    public int Version { get; } = version;

    public Func<IServiceProvider, TenantKey, IMessageRouter<TEvent>>? RouterFactory { get; set; }

    public IReadOnlyList<IChannelRegistration<TEvent>> Channels => _channels;

    public IReadOnlyDictionary<int, Func<object, TEvent>> Upgrades => _upgrades;

    public MessageTypeDescriptor Descriptor =>
        new(Type, typeof(TEvent), [.. _channels.Select(static channel => channel.Channel).Distinct()]);

    public void AddChannel(IChannelRegistration<TEvent> channel) => _channels.Add(channel);

    public void AddUpgrade(int fromVersion, Func<object, TEvent> upgrade) => _upgrades.Add(fromVersion, upgrade);

    public IMessageRouter<TEvent>? CreateRouter(IServiceProvider tenantServices, TenantKey key, Type? openRouterType)
    {
        if (RouterFactory is not null)
        {
            return RouterFactory(tenantServices, key);
        }

        return openRouterType is null
            ? null
            : (IMessageRouter<TEvent>)ActivatorUtilities.CreateInstance(tenantServices, ClosedRouterType(openRouterType));
    }

    private Type ClosedRouterType(Type openRouterType) =>
        _closedOpenRouterType ??= openRouterType.MakeGenericType(typeof(TEvent));
}
