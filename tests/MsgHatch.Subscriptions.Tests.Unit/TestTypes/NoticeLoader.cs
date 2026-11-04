using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Pipeline;
using MsgHatch.Domain.Routing;

namespace MsgHatch.Subscriptions.Tests.Unit.TestTypes;

/// <summary>Gives one <see cref="Notice"/> with the id of the target for each route.</summary>
/// <typeparam name="TEvent">The type of the domain event.</typeparam>
public sealed class NoticeLoader<TEvent> : IPayloadLoader<TEvent, Notice>
{
    public ValueTask<ErrorOr<IReadOnlyList<Notice>>> LoadAsync(
        ReceivedMessage<TEvent> message, ReadOnlyMemory<MessageRoute> routes, CancellationToken ct) =>
        ValueTask.FromResult(ErrorOr<IReadOnlyList<Notice>>.ForValue([.. routes.ToArray().Select(static route => new Notice(route.Target.Id))]));
}
