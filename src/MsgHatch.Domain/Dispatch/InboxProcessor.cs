using System.Collections.Frozen;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Inbox;
using MsgHatch.Domain.Telemetry;

namespace MsgHatch.Domain.Dispatch;

/// <summary>The inbox processor, with one dispatch loop for the inbox (section 4.9).</summary>
internal sealed class InboxProcessor(DispatchLoop<ReceivedMessage> loop) : IInboxProcessor
{
    public static InboxProcessor Create(IServiceProvider services)
    {
        var options = services.GetRequiredService<IOptionsMonitor<DispatchOptions>>().Get(OptionNames.Inbox);
        var time = services.GetRequiredService<TimeProvider>();
        var handlers = services.GetRequiredService<MsgHatchSetup>().Messages
            .DistinctBy(static message => message.Type)
            .ToFrozenDictionary(static message => message.Type);
        var work = new InboxWork(
            services.GetRequiredService<IInboxDispatchStore>(), services.GetRequiredService<IServiceScopeFactory>(), handlers, options,
            services.GetRequiredService<IOptionsMonitor<RetryOptions>>(), time, Loggers.For<InboxProcessor>(services));
        var loop = new DispatchLoop<ReceivedMessage>(work, options, services.GetRequiredService<IDispatchSignal>(), time);
        services.GetRequiredService<MsgHatchMetrics>().ObserveQueue(DispatchTopic.Inbox.Name, () => loop.QueueLength);
        return new InboxProcessor(loop);
    }

    public ValueTask RunAsync(CancellationToken ct) => loop.RunAsync(ct);

    public ValueTask<ErrorOr<DispatchSummary>> ProcessPendingAsync(int batchSize, CancellationToken ct) =>
        batchSize < 1
            ? ValueTask.FromResult<ErrorOr<DispatchSummary>>(new Error(Errors.ValidationFailed, "batchSize is less than 1."))
            : Boundary.RunAsync(() => loop.RunOnceAsync(batchSize, ct), ct);
}
