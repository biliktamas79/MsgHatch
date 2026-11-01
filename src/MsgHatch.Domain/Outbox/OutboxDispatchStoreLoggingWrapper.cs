using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;

namespace MsgHatch.Domain.Outbox;

/// <summary>The logging wrapper of the outbox dispatch store (section 4.15). It never logs the note of a person.</summary>
internal sealed class OutboxDispatchStoreLoggingWrapper(
    IOutboxDispatchStore inner, ILogger<OutboxDispatchStoreLoggingWrapper> logger, TimeProvider time) : IOutboxDispatchStore
{
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<ErrorOr<ClaimResult<OutgoingMessage>>> ClaimDueAsync(
        ChannelId channel, int maxCount, DateTime nowUtc, TimeSpan lease, CancellationToken ct)
    {
        var started = time.GetTimestamp();
        var result = await inner.ClaimDueAsync(channel, maxCount, nowUtc, lease, ct);
        LogClaim(channel, result, time.GetElapsedTime(started).TotalMilliseconds);
        return result;
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<Error?> RecordTryAsync(
        OutgoingMessage message, PublishTry publishTry, DateTime? nextTryAtUtc, CancellationToken ct)
    {
        var started = time.GetTimestamp();
        var error = await inner.RecordTryAsync(message, publishTry, nextTryAtUtc, ct);
        LogRecordTry(message, publishTry, error, time.GetElapsedTime(started).TotalMilliseconds);
        return error;
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<ErrorOr<int>> DeleteTestDataAsync(CancellationToken ct)
    {
        var started = time.GetTimestamp();
        var result = await inner.DeleteTestDataAsync(ct);
        LogDeleteTestData(result, time.GetElapsedTime(started).TotalMilliseconds);
        return result;
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<ErrorOr<IReadOnlyList<ChannelDown>>> GetDownChannelsAsync(CancellationToken ct)
    {
        var started = time.GetTimestamp();
        var result = await inner.GetDownChannelsAsync(ct);
        LogGetDownChannels(result, time.GetElapsedTime(started).TotalMilliseconds);
        return result;
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<ErrorOr<bool>> SetChannelDownAsync(ChannelDown down, CancellationToken ct)
    {
        var started = time.GetTimestamp();
        var result = await inner.SetChannelDownAsync(down, ct);
        LogChange(nameof(SetChannelDownAsync), down.Channel, down.Reason, result, time.GetElapsedTime(started).TotalMilliseconds);
        return result;
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<ErrorOr<bool>> SetChannelUpAsync(ChannelId channel, ChannelDownReason? reason, CancellationToken ct)
    {
        var started = time.GetTimestamp();
        var result = await inner.SetChannelUpAsync(channel, reason, ct);
        LogChange(nameof(SetChannelUpAsync), channel, reason, result, time.GetElapsedTime(started).TotalMilliseconds);
        return result;
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<ErrorOr<bool>> TryStartChannelCheckAsync(
        ChannelId channel, DateTime nowUtc, DateTime nextCheckAtUtc, CancellationToken ct)
    {
        var started = time.GetTimestamp();
        var result = await inner.TryStartChannelCheckAsync(channel, nowUtc, nextCheckAtUtc, ct);
        LogChange(nameof(TryStartChannelCheckAsync), channel, ChannelDownReason.Errors, result, time.GetElapsedTime(started).TotalMilliseconds);
        return result;
    }

    private void LogClaim(ChannelId channel, ErrorOr<ClaimResult<OutgoingMessage>> result, double elapsedMs)
    {
        if (result.IsError)
        {
            var level = ErrorLogLevels.For(result.Error);
            Log.ClaimFailed(
                logger, level, channel.Name, elapsedMs, result.Error.Type.Code, result.Error.Type.Name, result.Error.Message);
        }
        else
        {
            var (count, down) = (result.Value.Messages.Count, result.Value.Down?.Reason);
            Log.ClaimSucceeded(logger, count, channel.Name, down, elapsedMs);
        }
    }

    private void LogRecordTry(OutgoingMessage message, PublishTry publishTry, Error? error, double elapsedMs)
    {
        if (error is null)
        {
            Log.RecordTrySucceeded(
                logger, publishTry.TryNumber, publishTry.Outcome, message.Delivery.Id, message.Channel.Name,
                message.Tenant?.Value, message.Flags, elapsedMs);
        }
        else
        {
            var level = ErrorLogLevels.For(error);
            Log.RecordTryFailed(
                logger, level, publishTry.TryNumber, publishTry.Outcome, message.Delivery.Id, message.Channel.Name,
                message.Tenant?.Value, message.Flags, elapsedMs, error.Type.Code, error.Type.Name, error.Message);
        }
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

    private void LogGetDownChannels(ErrorOr<IReadOnlyList<ChannelDown>> result, double elapsedMs)
    {
        if (result.IsError)
        {
            var level = ErrorLogLevels.For(result.Error);
            Log.GetDownChannelsFailed(
                logger, level, elapsedMs, result.Error.Type.Code, result.Error.Type.Name, result.Error.Message);
        }
        else
        {
            Log.GetDownChannelsSucceeded(logger, result.Value.Count, elapsedMs);
        }
    }

    private void LogChange(string operation, ChannelId channel, ChannelDownReason? reason, ErrorOr<bool> result, double elapsedMs)
    {
        if (result.IsError)
        {
            var level = ErrorLogLevels.For(result.Error);
            Log.ChannelStateChangeFailed(
                logger, level, operation, channel.Name, reason, elapsedMs, result.Error.Type.Code, result.Error.Type.Name,
                result.Error.Message);
        }
        else
        {
            Log.ChannelStateChangeSucceeded(logger, operation, channel.Name, reason, result.Value, elapsedMs);
        }
    }
}
