using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Pipeline;
using MsgHatch.Domain.Routing;

namespace MsgHatch.Subscriptions.Tests.Unit.TestTypes;

/// <summary>A payload loader that fails each load with <see cref="Errors.Unavailable"/>.</summary>
/// <typeparam name="TEvent">The type of the domain event.</typeparam>
public sealed class FailingNoticeEmailLoader<TEvent> : IPayloadLoader<TEvent, NoticeEmail>
{
    public ValueTask<ErrorOr<IReadOnlyList<NoticeEmail>>> LoadAsync(
        ReceivedMessage<TEvent> message, ReadOnlyMemory<MessageRoute> routes, CancellationToken ct) =>
        ValueTask.FromResult(ErrorOr<IReadOnlyList<NoticeEmail>>.ForError(new Error(Errors.Unavailable)));
}
