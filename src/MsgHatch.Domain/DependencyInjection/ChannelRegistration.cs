using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Pipeline;
using MsgHatch.Domain.Routing;

namespace MsgHatch.Domain.DependencyInjection;

/// <summary>
/// One <c>As&lt;Channel&gt;</c> registration of a message type (section 4.3). It gets the composer of its channel with the
/// tenant key of the event, calls it, and builds the outgoing messages (section 4.8, step 5).
/// </summary>
/// <typeparam name="TEvent">The type of the domain event.</typeparam>
/// <typeparam name="TEnvelope">The type of the channel envelope.</typeparam>
internal sealed class ChannelRegistration<TEvent, TEnvelope>(
    ChannelId channel, string contentType, int version, IReadOnlyDictionary<int, Func<object, TEnvelope>> upgrades)
    : IChannelRegistration<TEvent>
    where TEnvelope : class
{
    private const int _schemaVersion = 1;

    public ChannelId Channel { get; } = channel;

    public string ContentType { get; } = contentType;

    public int Version { get; } = version;

    public IReadOnlyDictionary<int, Func<object, TEnvelope>> Upgrades { get; } = upgrades;

    public async ValueTask<ErrorOr<IReadOnlyList<ErrorOr<OutgoingMessage>>>> ComposeAsync(
        IServiceProvider services, TenantKey key, ReceivedMessage<TEvent> received, ReadOnlyMemory<MessageRoute> routes,
        OutgoingDefaults defaults, CancellationToken ct)
    {
        var composer = services.GetRequiredKeyedService<IEnvelopeComposer<TEvent, TEnvelope>>(key);
        var envelopes = await composer.ComposeAsync(received, routes, ct);
        if (envelopes.IsError)
        {
            return envelopes.Error;
        }

        if (envelopes.Value.Count != routes.Length)
        {
            return new Error(
                Errors.ValidationFailed, $"The composer of {ContentType} gave {envelopes.Value.Count} envelopes for {routes.Length} routes.");
        }

        var retry = defaults.RetryOptions.Get(ContentType);
        var messages = new ErrorOr<OutgoingMessage>[routes.Length];
        for (var index = 0; index < messages.Length; index++)
        {
            messages[index] = ToMessage(received, routes.Span[index], envelopes.Value[index], retry, defaults);
        }

        return ErrorOr<IReadOnlyList<ErrorOr<OutgoingMessage>>>.ForValue(messages);
    }

    private ErrorOr<OutgoingMessage> ToMessage(
        ReceivedMessage<TEvent> received, MessageRoute route, ErrorOr<TEnvelope> envelope, RetryOptions retry,
        OutgoingDefaults defaults)
    {
        if (envelope.IsError)
        {
            return envelope.Error;
        }

        if (envelope.Value is null)
        {
            return new Error(Errors.ValidationFailed, $"The composer of {ContentType} gave no envelope.");
        }

        var id = DeterministicId.Create(received.Delivery.Id, received.Tenant, route.Target, Channel);
        var delivery = new DeliveryState(
            id, DeliveryStatus.Pending, TryCount: 0, retry.MaxTryCount, defaults.NowUtc, LastTryAtUtc: null,
            NextTryAtUtc: defaults.NowUtc, LeaseUntilUtc: null, ExpiresAtUtc: defaults.NowUtc + retry.Expiry, LastError: null);
        return new OutgoingMessage<TEnvelope>(
            _schemaVersion, delivery, received.Delivery.Id, received.Tenant, received.Type, Channel, route.Target, ContentType,
            Version, received.Flags, defaults.TraceParent, route.Trace, envelope.Value);
    }
}
