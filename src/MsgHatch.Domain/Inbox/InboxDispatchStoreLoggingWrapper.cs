using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Failures;

namespace MsgHatch.Domain.Inbox;

/// <summary>The logging wrapper of the inbox dispatch store (section 4.15). It logs the tenant of each message, and never the event.</summary>
internal sealed class InboxDispatchStoreLoggingWrapper(
    IInboxDispatchStore inner, ILogger<InboxDispatchStoreLoggingWrapper> logger, TimeProvider time) : IInboxDispatchStore
{
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<ErrorOr<ClaimResult<ReceivedMessage>>> ClaimDueAsync(
        int maxCount, DateTime nowUtc, TimeSpan lease, CancellationToken ct)
    {
        var started = time.GetTimestamp();
        var result = await inner.ClaimDueAsync(maxCount, nowUtc, lease, ct);
        LogClaim(result, time.GetElapsedTime(started).TotalMilliseconds);
        return result;
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<Error?> CompleteAsync(ReceivedMessage message, DateTime triedAtUtc, CancellationToken ct)
    {
        var started = time.GetTimestamp();
        var error = await inner.CompleteAsync(message, triedAtUtc, ct);
        LogRecord(nameof(CompleteAsync), message, error, time.GetElapsedTime(started).TotalMilliseconds);
        return error;
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<Error?> FailAsync(
        ReceivedMessage message, string error, DateTime triedAtUtc, DateTime? nextTryAtUtc, CancellationToken ct)
    {
        var started = time.GetTimestamp();
        var failure = await inner.FailAsync(message, error, triedAtUtc, nextTryAtUtc, ct);
        LogRecord(nameof(FailAsync), message, failure, time.GetElapsedTime(started).TotalMilliseconds);
        return failure;
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<ErrorOr<int>> DeleteTestDataAsync(CancellationToken ct)
    {
        var started = time.GetTimestamp();
        var result = await inner.DeleteTestDataAsync(ct);
        LogDeleteTestData(result, time.GetElapsedTime(started).TotalMilliseconds);
        return result;
    }

    private void LogClaim(ErrorOr<ClaimResult<ReceivedMessage>> result, double elapsedMs)
    {
        if (result.IsError)
        {
            var level = ErrorLogLevels.For(result.Error);
            Log.ClaimFailed(logger, level, elapsedMs, result.Error.Type.Code, result.Error.Type.Name, result.Error.Message);
        }
        else
        {
            Log.ClaimSucceeded(logger, result.Value.Messages.Count, elapsedMs);
        }
    }

    private void LogRecord(string operation, ReceivedMessage message, Error? error, double elapsedMs)
    {
        if (error is null)
        {
            Log.RecordSucceeded(
                logger, operation, message.Delivery.Id, message.Type.Name, message.Tenant?.Value, message.Flags, elapsedMs);
        }
        else
        {
            var level = ErrorLogLevels.For(error);
            Log.RecordFailed(
                logger, level, operation, message.Delivery.Id, message.Type.Name, message.Tenant?.Value, message.Flags,
                elapsedMs, error.Type.Code, error.Type.Name, error.Message);
        }
    }

    private void LogDeleteTestData(ErrorOr<int> result, double elapsedMs)
    {
        if (result.IsError)
        {
            var level = ErrorLogLevels.For(result.Error);
            Log.DeleteTestDataFailed(logger, level, elapsedMs, result.Error.Type.Code, result.Error.Type.Name, result.Error.Message);
        }
        else
        {
            Log.DeleteTestDataSucceeded(logger, result.Value, elapsedMs);
        }
    }
}
