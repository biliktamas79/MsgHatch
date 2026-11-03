namespace MsgHatch.Broker;

/// <summary>
/// The channel envelope of a broker message: the template payload as it is, and the headers of the channel settings of
/// the route (D17, D135). The broker transport adds the headers of the library.
/// </summary>
/// <typeparam name="TPayload">The type of the template payload.</typeparam>
/// <param name="Payload">The payload.</param>
/// <param name="Headers">The headers of the channel settings of the route, or null for none.</param>
public sealed record BrokerEnvelope<TPayload>(TPayload Payload, IReadOnlyDictionary<string, string>? Headers = null) : IBrokerEnvelope
    where TPayload : class
{
    object IBrokerEnvelope.Payload => Payload;
}
