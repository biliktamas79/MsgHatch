using System.Collections.Concurrent;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Pipeline;
using MsgHatch.Domain.Routing;

namespace MsgHatch.Domain.Tests.Performance;

/// <summary>
/// The composer of the benchmark channel. It gives one envelope for all routes, from a list that it made one time for
/// each number of routes, so the composer allocates nothing.
/// </summary>
public sealed class PerfComposer : IEnvelopeComposer<PerfEvent, PerfEnvelope>
{
    private static readonly PerfEnvelope _envelope = new("Your order is on its way.");
    private static readonly ConcurrentDictionary<int, ValueTask<ErrorOr<IReadOnlyList<ErrorOr<PerfEnvelope>>>>> _envelopes = new();

    public ValueTask<ErrorOr<IReadOnlyList<ErrorOr<PerfEnvelope>>>> ComposeAsync(
        ReceivedMessage<PerfEvent> message, ReadOnlyMemory<MessageRoute> routes, CancellationToken ct) =>
        _envelopes.GetOrAdd(routes.Length, static count => ValueTask.FromResult(ErrorOr<IReadOnlyList<ErrorOr<PerfEnvelope>>>.ForValue(
            [.. Enumerable.Repeat(ErrorOr<PerfEnvelope>.ForValue(_envelope), count)])));
}
