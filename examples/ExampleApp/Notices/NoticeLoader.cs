using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Pipeline;
using MsgHatch.Domain.Routing;

namespace ExampleApp.Notices;

/// <summary>Gives one <see cref="Notice"/> for all routes, because its data does not depend on the target.</summary>
/// <typeparam name="TEvent">The example event.</typeparam>
public sealed class NoticeLoader<TEvent> : IPayloadLoader<TEvent, Notice>
    where TEvent : IExampleEvent
{
    /// <inheritdoc/>
    public ValueTask<ErrorOr<IReadOnlyList<Notice>>> LoadAsync(
        ReceivedMessage<TEvent> message, ReadOnlyMemory<MessageRoute> routes, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(message);
        var notice = new Notice(message.Type.Name, message.Event.Text);
        return ValueTask.FromResult(ErrorOr<IReadOnlyList<Notice>>.ForValue([.. Enumerable.Repeat(notice, routes.Length)]));
    }
}
