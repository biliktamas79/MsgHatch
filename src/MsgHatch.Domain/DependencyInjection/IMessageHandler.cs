using MsgHatch.Domain.Routing;

namespace MsgHatch.Domain.DependencyInjection;

/// <summary>The registration of one message type, with no generic type (section 4.3).</summary>
internal interface IMessageHandler
{
    MessageTypeDescriptor Descriptor { get; }
}
