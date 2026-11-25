using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Pipeline;
using MsgHatch.Domain.Routing;

namespace MsgHatch.Channels.Email.Tests.Performance;

/// <summary>Gives one payload that it made before the measurement, so the loader allocates nothing.</summary>
public sealed class PerfPayloadLoader : IPayloadLoader<string, PerfPayload>
{
    private static readonly ValueTask<ErrorOr<IReadOnlyList<PerfPayload>>> _payloads =
        ValueTask.FromResult(ErrorOr<IReadOnlyList<PerfPayload>>.ForValue([new PerfPayload("Ádám & <Éva>", 3, 129.90m)]));

    public ValueTask<ErrorOr<IReadOnlyList<PerfPayload>>> LoadAsync(
        ReceivedMessage<string> message, ReadOnlyMemory<MessageRoute> routes, CancellationToken ct) => _payloads;
}
