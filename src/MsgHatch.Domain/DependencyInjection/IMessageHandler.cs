using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Pipeline;
using MsgHatch.Domain.Routing;

namespace MsgHatch.Domain.DependencyInjection;

/// <summary>The registration of one message type, with no generic type (section 4.3).</summary>
internal interface IMessageHandler
{
    MessageType Type { get; }

    MessageTypeDescriptor Descriptor { get; }

    /// <summary>Resolves the router of <c>TenantKey.None</c> in the given DI scope.</summary>
    /// <returns>Null when the router resolves, else the text of the failure.</returns>
    string? RouterFailure(IServiceProvider scopedServices);

    /// <summary>Gives the breaks of the rules of the channel registrations and the payload loaders (D135).</summary>
    IEnumerable<string> RegistrationFailures();

    /// <summary>
    /// Upgrades a claimed received message of an old version (section 4.13), and runs the steps 2 to 6 for it in the
    /// given DI scope, with the tenant of the message (D126).
    /// </summary>
    ValueTask<ErrorOr<NotificationResult>> ProcessAsync(ReceivedMessage message, IServiceProvider scopedServices, CancellationToken ct);
}
