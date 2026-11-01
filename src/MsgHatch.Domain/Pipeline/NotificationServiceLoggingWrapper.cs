using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;

namespace MsgHatch.Domain.Pipeline;

/// <summary>The logging wrapper of the notification service (section 4.15). It logs the tenant of the message context.</summary>
internal sealed class NotificationServiceLoggingWrapper(
    INotificationService inner, ILogger<NotificationServiceLoggingWrapper> logger, TimeProvider time) : INotificationService
{
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<ErrorOr<NotificationResult>> ProcessAsync<TEvent>(
        TEvent domainEvent, MessageContext context, CancellationToken ct)
    {
        var started = time.GetTimestamp();
        var result = await inner.ProcessAsync(domainEvent, context, ct);
        LogProcess(typeof(TEvent).Name, context, result, time.GetElapsedTime(started).TotalMilliseconds);
        return result;
    }

    private void LogProcess(string eventType, MessageContext context, ErrorOr<NotificationResult> result, double elapsedMs)
    {
        if (result.IsError)
        {
            var level = ErrorLogLevels.For(result.Error);
            Log.ProcessFailed(
                logger, level, context.MessageId, eventType, context.Tenant?.Value, context.Flags, elapsedMs,
                result.Error.Type.Code, result.Error.Type.Name, result.Error.Message);
        }
        else
        {
            var (stored, failed) = (result.Value.Stored, result.Value.Failed);
            Log.ProcessSucceeded(
                logger, context.MessageId, eventType, context.Tenant?.Value, context.Flags, stored, failed, elapsedMs);
        }
    }
}
