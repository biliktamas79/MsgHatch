using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using MsgHatch.Domain.Failures;

namespace MsgHatch.Domain.Outbox;

/// <summary>The logging wrapper of the outbox dispatch store (section 4.15).</summary>
internal sealed class OutboxDispatchStoreLoggingWrapper(
    IOutboxDispatchStore inner, ILogger<OutboxDispatchStoreLoggingWrapper> logger, TimeProvider time) : IOutboxDispatchStore
{
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<ErrorOr<int>> DeleteTestDataAsync(CancellationToken ct)
    {
        var started = time.GetTimestamp();
        var result = await inner.DeleteTestDataAsync(ct);
        LogDeleteTestData(result, time.GetElapsedTime(started).TotalMilliseconds);
        return result;
    }

    private void LogDeleteTestData(ErrorOr<int> result, double elapsedMs)
    {
        if (result.IsError)
        {
            var level = ErrorLogLevels.For(result.Error);
            Log.DeleteTestDataFailed(
                logger, level, elapsedMs, result.Error.Type.Code, result.Error.Type.Name,
                result.Error.Message);
        }
        else
        {
            Log.DeleteTestDataSucceeded(logger, result.Value, elapsedMs);
        }
    }
}
