using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Management;

namespace MsgHatch.Domain.Inbox;

/// <summary>The logging wrapper of the inbox tenant store of one key (section 4.15). It logs the tenant of its key, and never the event.</summary>
internal sealed class InboxStoreLoggingWrapper(
    IInboxStore inner, TenantKey key, ILogger<InboxStoreLoggingWrapper> logger, TimeProvider time) : IInboxStore
{
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<Error?> AddAsync(ReceivedMessage message, CancellationToken ct)
    {
        var started = time.GetTimestamp();
        var error = await inner.AddAsync(message, ct);
        LogAdd(message, error, time.GetElapsedTime(started).TotalMilliseconds);
        return error;
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<ErrorOr<MessagePage<ReceivedMessageSummary>>> QueryAsync(InboxQuery query, CancellationToken ct)
    {
        var started = time.GetTimestamp();
        var result = await inner.QueryAsync(query, ct);
        LogManagement(nameof(QueryAsync), result.Error, result.IsError ? 0 : result.Value.Items.Count, started);
        return result;
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<ErrorOr<ReceivedMessage?>> GetAsync(Guid id, CancellationToken ct)
    {
        var started = time.GetTimestamp();
        var result = await inner.GetAsync(id, ct);
        LogManagement(nameof(GetAsync), result.Error, result.IsError || result.Value is null ? 0 : 1, started);
        return result;
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<ErrorOr<IReadOnlyList<MessageCount>>> CountAsync(TrafficQuery query, CancellationToken ct)
    {
        var started = time.GetTimestamp();
        var result = await inner.CountAsync(query, ct);
        LogManagement(nameof(CountAsync), result.Error, result.IsError ? 0 : result.Value.Count, started);
        return result;
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<ErrorOr<int>> RetryAsync(IReadOnlyList<Guid> ids, DateTime nowUtc, int addedTries, CancellationToken ct)
    {
        var started = time.GetTimestamp();
        var result = await inner.RetryAsync(ids, nowUtc, addedTries, ct);
        LogManagement(nameof(RetryAsync), result.Error, result.IsError ? 0 : result.Value, started);
        return result;
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<ErrorOr<int>> CancelAsync(IReadOnlyList<Guid> ids, DateTime nowUtc, CancellationToken ct)
    {
        var started = time.GetTimestamp();
        var result = await inner.CancelAsync(ids, nowUtc, ct);
        LogManagement(nameof(CancelAsync), result.Error, result.IsError ? 0 : result.Value, started);
        return result;
    }

    private void LogManagement(string operation, Error? error, int count, long started)
    {
        var elapsedMs = time.GetElapsedTime(started).TotalMilliseconds;
        if (error is null)
        {
            logger.TenantManagementSucceeded(operation, key.Tenant?.Value, count, elapsedMs);
        }
        else
        {
            var level = ErrorLogLevels.For(error);
            logger.TenantManagementFailed(level, operation, key.Tenant?.Value, elapsedMs, error.Type.Code, error.Type.Name, error.Message);
        }
    }

    private void LogAdd(ReceivedMessage message, Error? error, double elapsedMs)
    {
        if (error is null)
        {
            logger.AddSucceeded(message.Delivery.Id, message.Type.Name, key.Tenant?.Value, message.Flags, elapsedMs);
        }
        else
        {
            var level = ErrorLogLevels.For(error);
            logger.AddFailed(
                level, message.Delivery.Id, message.Type.Name, key.Tenant?.Value, message.Flags, elapsedMs,
                error.Type.Code, error.Type.Name, error.Message);
        }
    }
}
