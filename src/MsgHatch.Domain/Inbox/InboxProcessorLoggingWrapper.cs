using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Failures;

namespace MsgHatch.Domain.Inbox;

/// <summary>
/// The logging wrapper of the inbox processor (section 4.15). It logs the start and the stop of the loop at the
/// Information level. The wrappers of the store and the routers log each claim and each event.
/// </summary>
internal sealed class InboxProcessorLoggingWrapper(
    IInboxProcessor inner, ILogger<InboxProcessorLoggingWrapper> logger, TimeProvider time) : IInboxProcessor
{
    public async ValueTask RunAsync(CancellationToken ct)
    {
        logger.ProcessorStarted();
        await inner.RunAsync(ct);
        logger.ProcessorStopped();
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<ErrorOr<DispatchSummary>> ProcessPendingAsync(int batchSize, CancellationToken ct)
    {
        var started = time.GetTimestamp();
        var result = await inner.ProcessPendingAsync(batchSize, ct);
        LogProcessPending(result, time.GetElapsedTime(started).TotalMilliseconds);
        return result;
    }

    private void LogProcessPending(ErrorOr<DispatchSummary> result, double elapsedMs)
    {
        if (result.IsError)
        {
            var level = ErrorLogLevels.For(result.Error);
            logger.ProcessPendingFailed(level, elapsedMs, result.Error.Type.Code, result.Error.Type.Name, result.Error.Message);
        }
        else
        {
            var summary = result.Value;
            logger.ProcessPendingSucceeded(summary.Claimed, summary.Succeeded, summary.Retried, summary.Failed, elapsedMs);
        }
    }
}
