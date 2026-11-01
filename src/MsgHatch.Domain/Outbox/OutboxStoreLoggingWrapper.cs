using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;

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

    private void LogAdd(IReadOnlyList<OutgoingMessage> messages, Error? error, double elapsedMs)
    {
        var flags = messages.Count > 0 ? messages[0].Flags : MessageFlags.None;
        if (error is null)
        {
            Log.AddSucceeded(logger, messages.Count, key.Tenant?.Value, flags, elapsedMs);
        }
        else
        {
            var level = ErrorLogLevels.For(error);
            Log.AddFailed(
                logger, level, messages.Count, key.Tenant?.Value, flags, elapsedMs,
                error.Type.Code, error.Type.Name, error.Message);
        }
    }
}
