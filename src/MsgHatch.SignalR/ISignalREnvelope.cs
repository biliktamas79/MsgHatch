namespace MsgHatch.SignalR;

/// <summary>The channel envelope of a SignalR message with no generic type, for the transport (D135).</summary>
public interface ISignalREnvelope
{
    /// <summary>The payload, which the transport sends as the one argument of the method.</summary>
    object Payload { get; }
}
