using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Pipeline;
using MsgHatch.Domain.Routing;

namespace MsgHatch.Domain.Tests.Unit.TestTypes;

/// <summary>A second loader type of <see cref="TestPayload"/>, which the startup validation rejects next to <see cref="TestPayloadLoader"/> (D135).</summary>
public sealed class OtherTestPayloadLoader : IPayloadLoader<TestEvent, TestPayload>
{
    public ValueTask<ErrorOr<IReadOnlyList<TestPayload>>> LoadAsync(
        ReceivedMessage<TestEvent> message, ReadOnlyMemory<MessageRoute> routes, CancellationToken ct) =>
        ValueTask.FromResult(ErrorOr<IReadOnlyList<TestPayload>>.ForValue([]));
}
