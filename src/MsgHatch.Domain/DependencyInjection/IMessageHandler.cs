using MsgHatch.Domain.Routing;

namespace MsgHatch.Domain.DependencyInjection;

/// <summary>The registration of one message type, with no generic type (section 4.3).</summary>
internal interface IMessageHandler
{
    MessageTypeDescriptor Descriptor { get; }

    /// <summary>Resolves the router of <c>TenantKey.None</c> in the given DI scope.</summary>
    /// <returns>Null when the router resolves, else the text of the failure.</returns>
    string? RouterFailure(IServiceProvider scopedServices);
}
