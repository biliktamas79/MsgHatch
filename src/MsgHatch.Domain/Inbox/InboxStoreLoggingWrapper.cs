using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;

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

    private void LogAdd(ReceivedMessage message, Error? error, double elapsedMs)
    {
        if (error is null)
        {
            Log.AddSucceeded(logger, message.Delivery.Id, message.Type.Name, key.Tenant?.Value, message.Flags, elapsedMs);
        }
        else
        {
            var level = ErrorLogLevels.For(error);
            Log.AddFailed(
                logger, level, message.Delivery.Id, message.Type.Name, key.Tenant?.Value, message.Flags, elapsedMs,
                error.Type.Code, error.Type.Name, error.Message);
        }
    }
}
