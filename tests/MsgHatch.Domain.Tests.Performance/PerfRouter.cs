using System.Collections.Concurrent;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Routing;

namespace MsgHatch.Domain.Tests.Performance;

/// <summary>
/// The router of the benchmarks. It does nothing else than give the routes of its event, from a list that it made one
/// time for each number of routes, so the router allocates nothing.
/// </summary>
public sealed class PerfRouter : IMessageRouter<PerfEvent>
{
    private static readonly ConcurrentDictionary<int, ValueTask<ErrorOr<IReadOnlyList<MessageRoute>>>> _routes = new();

    public ValueTask<ErrorOr<IReadOnlyList<MessageRoute>>> RouteAsync(ReceivedMessage<PerfEvent> message, CancellationToken ct) =>
        _routes.GetOrAdd(message.Event.Routes, static count => ValueTask.FromResult(ErrorOr<IReadOnlyList<MessageRoute>>.ForValue(RoutesOf(count))));

    private static MessageRoute[] RoutesOf(int count) =>
        [.. Enumerable.Range(1, count).Select(static user => new MessageRoute(
            new DeliveryTarget(DeliveryTargetKind.User, $"u{user}"), PerfChannel.Id, new PerfSettings(), "en", $"user{user}@example.com",
            TemplateScope: null, Trace: null))];
}
