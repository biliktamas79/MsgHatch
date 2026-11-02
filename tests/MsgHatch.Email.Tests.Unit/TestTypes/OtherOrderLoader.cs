using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Pipeline;
using MsgHatch.Domain.Routing;

namespace MsgHatch.Email.Tests.Unit.TestTypes;

/// <summary>The payload loader of the second message type: one payload for each route.</summary>
public sealed class OtherOrderLoader : IPayloadLoader<OtherTestEvent, OrderEmail>
{
    public ValueTask<ErrorOr<IReadOnlyList<OrderEmail>>> LoadAsync(
        ReceivedMessage<OtherTestEvent> message, ReadOnlyMemory<MessageRoute> routes, CancellationToken ct) =>
        ValueTask.FromResult(ErrorOr<IReadOnlyList<OrderEmail>>.ForValue(
            [.. routes.ToArray().Select(route => new OrderEmail(route.Target.Id, 1, 1m, TestMessages.CreatedAtUtc))]));
}
