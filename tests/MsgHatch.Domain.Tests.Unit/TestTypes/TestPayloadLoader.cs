using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Pipeline;
using MsgHatch.Domain.Routing;

namespace MsgHatch.Domain.Tests.Unit.TestTypes;

/// <summary>A payload loader of <see cref="TestPayload"/>: one payload for each route.</summary>
public sealed class TestPayloadLoader : IPayloadLoader<TestEvent, TestPayload>
{
    public ValueTask<ErrorOr<IReadOnlyList<TestPayload>>> LoadAsync(
        ReceivedMessage<TestEvent> message, ReadOnlyMemory<MessageRoute> routes, CancellationToken ct) =>
        ValueTask.FromResult(ErrorOr<IReadOnlyList<TestPayload>>.ForValue([.. routes.ToArray().Select(route => new TestPayload(route.Target.Id))]));
}
