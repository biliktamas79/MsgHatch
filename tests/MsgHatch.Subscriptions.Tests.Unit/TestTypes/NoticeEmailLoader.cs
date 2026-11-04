using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Pipeline;
using MsgHatch.Domain.Routing;

namespace MsgHatch.Subscriptions.Tests.Unit.TestTypes;

/// <summary>Gives one <see cref="NoticeEmail"/> with the id of the target for each route.</summary>
/// <typeparam name="TEvent">The type of the domain event.</typeparam>
public sealed class NoticeEmailLoader<TEvent> : IPayloadLoader<TEvent, NoticeEmail>
{
    public ValueTask<ErrorOr<IReadOnlyList<NoticeEmail>>> LoadAsync(
        ReceivedMessage<TEvent> message, ReadOnlyMemory<MessageRoute> routes, CancellationToken ct) =>
        ValueTask.FromResult(ErrorOr<IReadOnlyList<NoticeEmail>>.ForValue([.. routes.ToArray().Select(static route => new NoticeEmail(route.Target.Id))]));
}
