using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Routing;

namespace MsgHatch.Domain.Pipeline;

/// <summary>The logging wrapper of the router of one key (section 4.15). It logs the tenant of its key.</summary>
/// <typeparam name="TEvent">The type of the domain event.</typeparam>
internal sealed class MessageRouterLoggingWrapper<TEvent>(
    IMessageRouter<TEvent> inner, TenantKey key, ILogger<MessageRouterLoggingWrapper<TEvent>> logger, TimeProvider time)
    : IMessageRouter<TEvent>
{
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<ErrorOr<IReadOnlyList<MessageRoute>>> RouteAsync(ReceivedMessage<TEvent> message, CancellationToken ct)
    {
        var started = time.GetTimestamp();
        var result = await inner.RouteAsync(message, ct);
        LogRoute(message, result, time.GetElapsedTime(started).TotalMilliseconds);
        return result;
    }

    private void LogRoute(ReceivedMessage<TEvent> message, ErrorOr<IReadOnlyList<MessageRoute>> result, double elapsedMs)
    {
        if (result.IsError)
        {
            var level = ErrorLogLevels.For(result.Error);
            Log.RouteFailed(
                logger, level, message.Type.Name, message.Delivery.Id, key.Tenant?.Value, message.Flags, elapsedMs,
                result.Error.Type.Code, result.Error.Type.Name, result.Error.Message);
        }
        else
        {
            var routeCount = result.Value.Count;
            Log.RouteSucceeded(
                logger, message.Type.Name, routeCount, message.Delivery.Id, key.Tenant?.Value, message.Flags, elapsedMs);
        }
    }
}
