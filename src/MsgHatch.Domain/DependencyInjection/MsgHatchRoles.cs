namespace MsgHatch.Domain.DependencyInjection;

/// <summary>
/// The roles of a host (D105). They tell the startup validation what the host must have. The DI registrations do not
/// change with the role, so all processes call the same registration code.
/// </summary>
[Flags]
public enum MsgHatchRoles
{
    /// <summary>
    /// No role: the host only calls the API clients, so it needs no router, no transport, and no store. The setup check gives
    /// an error for each message type that it registers (D230).
    /// </summary>
    None = 0,

    /// <summary>The host routes and composes the events: it runs the inbox processor, or calls <c>ProcessAsync</c>.</summary>
    Consumer = 1,

    /// <summary>The host sends the messages of the channels of its transports: it runs the outbox dispatcher.</summary>
    Publisher = 2,
}
