using MsgHatch.Domain.Delivery;

namespace MsgHatch.Domain.Persistence;

/// <summary>
/// The typed code of a content type entry of a message: it reads the content of a typed message and makes a typed message,
/// so <see cref="MsgHatchJson"/> needs no generic type that it closes at run time (D237).
/// </summary>
internal abstract class MessageShape;

/// <summary>The shape of a received message.</summary>
internal abstract class ReceivedShape : MessageShape
{
    public abstract object? EventOf(ReceivedMessage message);

    public abstract ReceivedMessage Create(MsgHatchJson.ReceivedHeader header, object content);
}

/// <summary>The shape of the received message of <typeparamref name="TEvent"/>.</summary>
internal sealed class ReceivedShape<TEvent> : ReceivedShape
{
    public static ReceivedShape<TEvent> Instance { get; } = new();

    public override object? EventOf(ReceivedMessage message) => ((ReceivedMessage<TEvent>)message).Event;

    public override ReceivedMessage Create(MsgHatchJson.ReceivedHeader header, object content) =>
        new ReceivedMessage<TEvent>(
            header.SchemaVersion, header.Delivery, header.Tenant, header.Type, header.ContentVersion, header.Flags,
            header.TraceParent, (TEvent)content);
}

/// <summary>The shape of an outgoing message.</summary>
internal abstract class OutgoingShape : MessageShape
{
    public abstract OutgoingMessage Create(MsgHatchJson.OutgoingHeader header, object content);
}

/// <summary>The shape of the outgoing message of <typeparamref name="TEnvelope"/>.</summary>
internal sealed class OutgoingShape<TEnvelope> : OutgoingShape
    where TEnvelope : class
{
    public static OutgoingShape<TEnvelope> Instance { get; } = new();

    public override OutgoingMessage Create(MsgHatchJson.OutgoingHeader header, object content) =>
        new OutgoingMessage<TEnvelope>(
            header.SchemaVersion, header.Delivery, header.SourceMessageId, header.Tenant, header.Type, header.Channel,
            header.Target, header.ContentType, header.ContentVersion, header.Flags, header.TraceParent, header.Trace,
            (TEnvelope)content);
}
