using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Pipeline;
using MsgHatch.Domain.Routing;

namespace MsgHatch.Domain.DependencyInjection;

/// <summary>The registration of the message type <typeparamref name="TEvent"/> with its channel registrations (section 4.3).</summary>
/// <typeparam name="TEvent">The type of the domain event.</typeparam>
internal sealed class MessageRegistration<TEvent>(MessageType type, int version) : IMessageHandler
{
    private const int _schemaVersion = 1;

    private readonly List<IChannelRegistration<TEvent>> _channels = [];
    private readonly Dictionary<int, Func<ReceivedMessage, TEvent>> _upgrades = [];
    private readonly List<(Type Payload, Type? Loader)> _loaders = [];
    private Type? _closedOpenRouterType;

    public MessageType Type { get; } = type;

    public int Version { get; } = version;

    public Func<IServiceProvider, TenantKey, IMessageRouter<TEvent>>? RouterFactory { get; set; }

    public MessageTypeDescriptor Descriptor =>
        new(Type, typeof(TEvent), [.. _channels.Select(static channel => channel.Channel).Distinct()]);

    public IEnumerable<IEnvelopeUpgrade> EnvelopeUpgrades => _channels;

    public void AddChannel(IChannelRegistration<TEvent> channel) => _channels.Add(channel);

    public void AddUpgrade(int fromVersion, Func<ReceivedMessage, TEvent> upgrade) => _upgrades.Add(fromVersion, upgrade);

    /// <summary>Records a payload loader. A factory has no loader type, so each factory counts as one more loader.</summary>
    /// <returns>False when the same loader type is already registered for the payload type (D135).</returns>
    public bool TryAddLoader(Type payload, Type? loader)
    {
        if (loader is not null && _loaders.Contains((payload, loader)))
        {
            return false;
        }

        _loaders.Add((payload, loader));
        return true;
    }

    /// <summary>The breaks of the rules of D135: one registration for each channel, one envelope type for each registration, and one loader for each payload type.</summary>
    public IEnumerable<string> RegistrationFailures() =>
    [
        .. _channels.GroupBy(static channel => channel.Channel).Where(static group => group.Count() > 1)
            .Select(group => $"The message type {Type.Name} has {group.Count()} registrations on the channel {group.Key.Name}."),
        .. _channels.GroupBy(static channel => channel.EnvelopeType).Where(static group => group.Count() > 1)
            .Select(group => $"The message type {Type.Name} has {group.Count()} channel registrations with the envelope type {group.Key.Name}."),
        .. _loaders.GroupBy(static loader => loader.Payload).Where(static group => group.Count() > 1)
            .Select(group => $"The payload type {group.Key.Name} of the message type {Type.Name} has {group.Count()} loaders."),
    ];

    public ValueTask<ErrorOr<NotificationResult>> ProcessAsync(
        ReceivedMessage message, IServiceProvider scopedServices, CancellationToken ct)
    {
        var current = Upgrade(message);
        return current.IsError
            ? ValueTask.FromResult<ErrorOr<NotificationResult>>(current.Error)
            : scopedServices.GetRequiredService<NotificationPipeline>().ProcessReceivedAsync(this, current.Value, ct);
    }

    private ErrorOr<ReceivedMessage<TEvent>> Upgrade(ReceivedMessage message) => message switch
    {
        ReceivedMessage<TEvent> current => current,
        _ when _upgrades.TryGetValue(message.ContentVersion, out var upgrade) => new ReceivedMessage<TEvent>(
            message.SchemaVersion, message.Delivery, message.Tenant, message.Type, Version, message.Flags, message.TraceParent,
            upgrade(message)),
        _ => new Error(Errors.UnsupportedVersion, $"The message type {Type.Name} has no upgrade from version {message.ContentVersion}."),
    };

    public IChannelRegistration<TEvent>? FindChannel(ChannelId channel) =>
        _channels.Find(registration => registration.Channel == channel);

    public ReceivedMessage<TEvent> Receive(
        TEvent domainEvent, MessageContext context, DateTime nowUtc, int maxTryCount, string? traceParent) =>
        new(
            _schemaVersion,
            new DeliveryState(
                context.MessageId, DeliveryStatus.Pending, TryCount: 0, maxTryCount, nowUtc, LastTryAtUtc: null,
                NextTryAtUtc: nowUtc, LeaseUntilUtc: null, ExpiresAtUtc: null, LastError: null),
            context.Tenant, Type, Version, context.Flags, traceParent, domainEvent);

    public string? RouterFailure(IServiceProvider scopedServices)
    {
        try
        {
            return scopedServices.GetKeyedService<IMessageRouter<TEvent>>(TenantKey.None) is null
                ? $"The message type {Type.Name} has no router. Select one with WithRouter or UseRouter."
                : null;
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            return $"The router of the message type {Type.Name} does not resolve: {exception.Message}";
        }
    }

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
