using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Failures;

namespace MsgHatch.Domain.Outbox;

/// <summary>
/// The logging wrapper of the outbox dispatcher (section 4.15). It logs the start and the stop of the loops at the
/// Information level. The wrappers of the store and the transports log each claim and each send.
/// </summary>
internal sealed class OutboxDispatcherLoggingWrapper(
    IOutboxDispatcher inner, ILogger<OutboxDispatcherLoggingWrapper> logger, TimeProvider time) : IOutboxDispatcher
{
    public async ValueTask RunAsync(CancellationToken ct)
    {
        Log.DispatcherStarted(logger);
        await inner.RunAsync(ct);
        Log.DispatcherStopped(logger);
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<ErrorOr<DispatchSummary>> DispatchPendingAsync(int batchSize, CancellationToken ct)
    {
        var started = time.GetTimestamp();
        var result = await inner.DispatchPendingAsync(batchSize, ct);
        LogDispatchPending(result, time.GetElapsedTime(started).TotalMilliseconds);
        return result;
    }

    private void LogDispatchPending(ErrorOr<DispatchSummary> result, double elapsedMs)
    {
        if (result.IsError)
        {
            var level = ErrorLogLevels.For(result.Error);
            Log.DispatchPendingFailed(logger, level, elapsedMs, result.Error.Type.Code, result.Error.Type.Name, result.Error.Message);
        }
        else
        {
            var summary = result.Value;
            Log.DispatchPendingSucceeded(logger, summary.Claimed, summary.Succeeded, summary.Retried, summary.Failed, elapsedMs);
        }
    }
}
