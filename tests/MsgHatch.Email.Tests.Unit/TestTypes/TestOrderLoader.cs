using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Pipeline;
using MsgHatch.Domain.Routing;

namespace MsgHatch.Email.Tests.Unit.TestTypes;

/// <summary>The payload loader of the registration tests: one payload for each route, with the target id as its name.</summary>
public sealed class TestOrderLoader : IPayloadLoader<TestEvent, OrderEmail>
{
    public ValueTask<ErrorOr<IReadOnlyList<OrderEmail>>> LoadAsync(
        ReceivedMessage<TestEvent> message, ReadOnlyMemory<MessageRoute> routes, CancellationToken ct) =>
        ValueTask.FromResult(ErrorOr<IReadOnlyList<OrderEmail>>.ForValue(
            [.. routes.ToArray().Select(route => new OrderEmail(route.Target.Id, 3, 1234.5m, TestMessages.CreatedAtUtc))]));
}
