using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Pipeline;
using MsgHatch.Domain.Routing;

namespace MsgHatch.Broker;

/// <summary>
/// The composer of a broker registration (section 4.8, step 4). It loads the payloads of all routes with one call, and
/// puts each payload as it is, with the headers of the channel settings of its route, into a
/// <see cref="BrokerEnvelope{TPayload}"/> (D17, D135). A composer is a tenant service (D126), so it gets the loader of
/// its key.
/// </summary>
/// <typeparam name="TEvent">The type of the domain event.</typeparam>
/// <typeparam name="TPayload">The type of the template payload.</typeparam>
/// <param name="loader">The payload loader of the registration.</param>
public sealed class BrokerComposer<TEvent, TPayload>(IPayloadLoader<TEvent, TPayload> loader)
    : IEnvelopeComposer<TEvent, BrokerEnvelope<TPayload>>
    where TPayload : class
{
    /// <inheritdoc/>
    public async ValueTask<ErrorOr<IReadOnlyList<ErrorOr<BrokerEnvelope<TPayload>>>>> ComposeAsync(
        ReceivedMessage<TEvent> message, ReadOnlyMemory<MessageRoute> routes, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(message);
        var payloads = await loader.LoadAsync(message, routes, ct);
        if (payloads.IsError)
        {
            return payloads.Error;
        }

        if (payloads.Value.Count != routes.Length)
        {
            return new Error(Errors.ValidationFailed, $"The payload loader gave {payloads.Value.Count} payloads for {routes.Length} routes.");
        }

        var routeArray = routes.ToArray();
        return ErrorOr<IReadOnlyList<ErrorOr<BrokerEnvelope<TPayload>>>>.ForValue(
            [.. payloads.Value.Select((payload, index) => EnvelopeOf(payload, routeArray[index]))]);
    }

    private static ErrorOr<BrokerEnvelope<TPayload>> EnvelopeOf(TPayload? payload, MessageRoute route) =>
        payload is null
            ? new Error(Errors.ValidationFailed, "The payload loader gave no payload for the route.")
            : new BrokerEnvelope<TPayload>(payload, (route.Settings as BrokerChannelSettings)?.Headers);
}
