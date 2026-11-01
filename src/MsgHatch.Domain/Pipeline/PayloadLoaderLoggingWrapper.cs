using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Routing;

namespace MsgHatch.Domain.Pipeline;

/// <summary>The logging wrapper of the payload loader of one key (section 4.15). It logs the tenant of its key, and never a payload value.</summary>
/// <typeparam name="TEvent">The type of the domain event.</typeparam>
/// <typeparam name="TPayload">The type of the template payload.</typeparam>
internal sealed class PayloadLoaderLoggingWrapper<TEvent, TPayload>(
    IPayloadLoader<TEvent, TPayload> inner, TenantKey key, ILogger<PayloadLoaderLoggingWrapper<TEvent, TPayload>> logger,
    TimeProvider time)
    : IPayloadLoader<TEvent, TPayload>
{
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<ErrorOr<IReadOnlyList<TPayload>>> LoadAsync(
        ReceivedMessage<TEvent> message, ReadOnlyMemory<MessageRoute> routes, CancellationToken ct)
    {
        var started = time.GetTimestamp();
        var result = await inner.LoadAsync(message, routes, ct);
        LogLoad(message, routes.Length, result, time.GetElapsedTime(started).TotalMilliseconds);
        return result;
    }

    private void LogLoad(ReceivedMessage<TEvent> message, int routeCount, ErrorOr<IReadOnlyList<TPayload>> result, double elapsedMs)
    {
        if (result.IsError)
        {
            var level = ErrorLogLevels.For(result.Error);
            Log.LoadFailed(
                logger, level, message.Type.Name, routeCount, message.Delivery.Id, key.Tenant?.Value, message.Flags,
                elapsedMs, result.Error.Type.Code, result.Error.Type.Name, result.Error.Message);
        }
        else
        {
            var payloadCount = result.Value.Count;
            Log.LoadSucceeded(
                logger, message.Type.Name, payloadCount, routeCount, message.Delivery.Id, key.Tenant?.Value, message.Flags,
                elapsedMs);
        }
    }
}
