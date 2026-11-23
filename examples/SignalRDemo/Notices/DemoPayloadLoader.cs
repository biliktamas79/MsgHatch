using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Pipeline;
using MsgHatch.Domain.Routing;

namespace SignalRDemo.Notices;

/// <summary>Gives one <see cref="DemoPayload"/> for each route, with the id of the notification, so a session can match it to the sent log.</summary>
public sealed class DemoPayloadLoader : IPayloadLoader<DemoNotice, DemoPayload>
{
    /// <inheritdoc/>
    public ValueTask<ErrorOr<IReadOnlyList<DemoPayload>>> LoadAsync(
        ReceivedMessage<DemoNotice> message, ReadOnlyMemory<MessageRoute> routes, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(message);
        var payloads = new DemoPayload[routes.Length];
        for (var index = 0; index < payloads.Length; index++)
        {
            var target = routes.Span[index].Target;
            payloads[index] = new DemoPayload(message.Delivery.Id, message.Tenant?.Value, target.Kind.ToString(), target.Id, message.Event.Text);
        }

        return ValueTask.FromResult(ErrorOr<IReadOnlyList<DemoPayload>>.ForValue(payloads));
    }
}
