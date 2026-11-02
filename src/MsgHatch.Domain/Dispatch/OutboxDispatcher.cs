using System.Collections.Frozen;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Outbox;
using MsgHatch.Domain.Telemetry;

namespace MsgHatch.Domain.Dispatch;

/// <summary>
/// The outbox dispatcher, with one dispatch loop for each channel whose transport the host registered (D101). Each loop
/// has its own claim loop, work queue, and workers, so a slow channel does not delay another channel.
/// </summary>
// ponytail: one DispatchOptions for all channel loops, add options for each channel if one process hosts channels with different needs
internal sealed class OutboxDispatcher(IReadOnlyList<DispatchLoop<OutgoingMessage>> loops) : IOutboxDispatcher
{
    public static OutboxDispatcher Create(IServiceProvider services)
    {
        var store = services.GetRequiredService<IOutboxDispatchStore>();
        var options = services.GetRequiredService<IOptionsMonitor<DispatchOptions>>().Get(OptionNames.Outbox);
        var retryOptions = services.GetRequiredService<IOptionsMonitor<RetryOptions>>();
        var signal = services.GetRequiredService<IDispatchSignal>();
        var time = services.GetRequiredService<TimeProvider>();
        var downOptions = services.GetRequiredService<IOptionsMonitor<ChannelDownOptions>>();
        var testDelivery = services.GetRequiredService<IOptionsMonitor<TestDeliveryOptions>>();
        var metrics = services.GetRequiredService<MsgHatchMetrics>();
        var logger = Loggers.For<OutboxDispatcher>(services);
        var upgrades = services.GetRequiredService<MsgHatchSetup>().Messages
            .SelectMany(static message => message.EnvelopeUpgrades)
            .DistinctBy(static upgrade => upgrade.ContentType, StringComparer.Ordinal)
            .ToFrozenDictionary(static upgrade => upgrade.ContentType, StringComparer.Ordinal);
        return new OutboxDispatcher(
        [
            .. services.GetServices<ITransport>().Select(transport => ObservedLoop(
                new OutboxChannelWork(
                    transport, store, upgrades, options, downOptions.Get(transport.Channel.Name),
                    testDelivery.Get(transport.Channel.Name), retryOptions, time, metrics, logger),
                options, signal, time, metrics)),
        ]);
    }

    private static DispatchLoop<OutgoingMessage> ObservedLoop(
        OutboxChannelWork work, DispatchOptions options, IDispatchSignal signal, TimeProvider time, MsgHatchMetrics metrics)
    {
        var loop = new DispatchLoop<OutgoingMessage>(work, options, signal, time);
        metrics.ObserveQueue(work.Channel.Name, () => loop.QueueLength);
        metrics.ObserveChannel(work.Channel, () => work.LastClaimWasDown);
        return loop;
    }

    public async ValueTask RunAsync(CancellationToken ct) =>
        await Task.WhenAll(loops.Select(loop => loop.RunAsync(ct).AsTask()));

    public ValueTask<ErrorOr<DispatchSummary>> DispatchPendingAsync(int batchSize, CancellationToken ct) =>
        batchSize < 1
            ? ValueTask.FromResult<ErrorOr<DispatchSummary>>(new Error(Errors.ValidationFailed, "batchSize is less than 1."))
            : Boundary.RunAsync(() => DispatchEachChannelAsync(batchSize, ct), ct);

    private async ValueTask<ErrorOr<DispatchSummary>> DispatchEachChannelAsync(int batchSize, CancellationToken ct)
    {
        var total = default(DispatchSummary);
        foreach (var loop in loops)
        {
            var summary = await loop.RunOnceAsync(batchSize, ct);
            if (summary.IsError)
            {
                return summary.Error;
            }

            total = new DispatchSummary(
                total.Claimed + summary.Value.Claimed, total.Succeeded + summary.Value.Succeeded,
                total.Retried + summary.Value.Retried, total.Failed + summary.Value.Failed);
        }

        return total;
    }
}
