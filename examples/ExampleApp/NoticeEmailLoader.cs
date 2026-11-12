using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Pipeline;
using MsgHatch.Domain.Routing;

namespace ExampleApp;

/// <summary>Gives one <see cref="NoticeEmail"/> for each route, with the text of the event and the id of the target.</summary>
/// <typeparam name="TEvent">The example event.</typeparam>
public sealed class NoticeEmailLoader<TEvent> : IPayloadLoader<TEvent, NoticeEmail>
    where TEvent : IExampleEvent
{
    /// <inheritdoc/>
    public ValueTask<ErrorOr<IReadOnlyList<NoticeEmail>>> LoadAsync(
        ReceivedMessage<TEvent> message, ReadOnlyMemory<MessageRoute> routes, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(message);
        var payloads = new NoticeEmail[routes.Length];
        for (var index = 0; index < payloads.Length; index++)
        {
            payloads[index] = new NoticeEmail(message.Event.Text, routes.Span[index].Target.Id);
        }

        return ValueTask.FromResult(ErrorOr<IReadOnlyList<NoticeEmail>>.ForValue(payloads));
    }
}
