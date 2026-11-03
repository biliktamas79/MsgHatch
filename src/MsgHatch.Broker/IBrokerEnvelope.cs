namespace MsgHatch.Broker;

/// <summary>The channel envelope of a broker message with no generic type, for the transport (D135).</summary>
public interface IBrokerEnvelope
{
    /// <summary>The payload, which the broker client publishes with its run-time type.</summary>
    object Payload { get; }

    /// <summary>The headers of the channel settings of the route, or null for none.</summary>
    IReadOnlyDictionary<string, string>? Headers { get; }
}
