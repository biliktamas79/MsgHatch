namespace MsgHatch.SignalR;

/// <summary>
/// The channel envelope of a SignalR message: the template payload (D17, D135). The target and the method name come
/// from the outgoing message.
/// </summary>
/// <typeparam name="TPayload">The type of the template payload.</typeparam>
/// <param name="Payload">The payload.</param>
public sealed record SignalREnvelope<TPayload>(TPayload Payload) : ISignalREnvelope
    where TPayload : class
{
    object ISignalREnvelope.Payload => Payload;
}
