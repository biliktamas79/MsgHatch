using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Management;

namespace MsgHatch.Domain.Outbox;

/// <summary>The logging wrapper of the outbox tenant store of one key (section 4.15). It logs the tenant of its key.</summary>
internal sealed class OutboxStoreLoggingWrapper(
    IOutboxStore inner, TenantKey key, ILogger<OutboxStoreLoggingWrapper> logger, TimeProvider time) : IOutboxStore
{
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<Error?> AddAsync(IReadOnlyList<OutgoingMessage> messages, CancellationToken ct)
    {
        var started = time.GetTimestamp();
        var error = await inner.AddAsync(messages, ct);
        LogAdd(messages, error, time.GetElapsedTime(started).TotalMilliseconds);
        return error;
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<ErrorOr<MessagePage<OutgoingMessageSummary>>> QueryAsync(OutboxQuery query, CancellationToken ct)
    {
        var started = time.GetTimestamp();
        var result = await inner.QueryAsync(query, ct);
        LogManagement(nameof(QueryAsync), result.Error, result.IsError ? 0 : result.Value.Items.Count, started);
        return result;
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<ErrorOr<OutgoingMessageHistory?>> GetAsync(Guid id, CancellationToken ct)
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

    private void LogAdd(IReadOnlyList<OutgoingMessage> messages, Error? error, double elapsedMs)
    {
        var flags = messages.Count > 0 ? messages[0].Flags : MessageFlags.None;
        if (error is null)
        {
            logger.AddSucceeded(messages.Count, key.Tenant?.Value, flags, elapsedMs);
        }
        else
        {
            var level = ErrorLogLevels.For(error);
            logger.AddFailed(
                level, messages.Count, key.Tenant?.Value, flags, elapsedMs,
                error.Type.Code, error.Type.Name, error.Message);
        }
    }
}
