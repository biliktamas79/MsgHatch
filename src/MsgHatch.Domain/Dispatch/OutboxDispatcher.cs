using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Outbox;

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
        var logger = Loggers.For<OutboxDispatcher>(services);
        return new OutboxDispatcher(
        [
            .. services.GetServices<ITransport>().Select(transport => new DispatchLoop<OutgoingMessage>(
                new OutboxChannelWork(
                    transport, store, options, downOptions.Get(transport.Channel.Name), testDelivery.Get(transport.Channel.Name),
                    retryOptions, time, logger),
                options, signal, time)),
        ]);
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
