using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Pipeline;
using MsgHatch.Domain.Routing;

namespace MsgHatch.SignalR;

/// <summary>
/// The composer of a SignalR registration (section 4.8, step 4). It loads the payloads of all routes with one call, and
/// puts each payload into a <see cref="SignalREnvelope{TPayload}"/> (D17, D135). A composer is a tenant service (D126),
/// so it gets the loader of its key.
/// </summary>
/// <typeparam name="TEvent">The type of the domain event.</typeparam>
/// <typeparam name="TPayload">The type of the template payload.</typeparam>
/// <param name="loader">The payload loader of the registration.</param>
public sealed class SignalRComposer<TEvent, TPayload>(IPayloadLoader<TEvent, TPayload> loader)
    : IEnvelopeComposer<TEvent, SignalREnvelope<TPayload>>
    where TPayload : class
{
    /// <inheritdoc/>
    public async ValueTask<ErrorOr<IReadOnlyList<ErrorOr<SignalREnvelope<TPayload>>>>> ComposeAsync(
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

        return ErrorOr<IReadOnlyList<ErrorOr<SignalREnvelope<TPayload>>>>.ForValue([.. payloads.Value.Select(EnvelopeOf)]);
    }

    private static ErrorOr<SignalREnvelope<TPayload>> EnvelopeOf(TPayload? payload) =>
        payload is null
            ? new Error(Errors.ValidationFailed, "The payload loader gave no payload for the route.")
            : new SignalREnvelope<TPayload>(payload);
}
