using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;

namespace MsgHatch.Domain.Management;

/// <summary>
/// The logging wrapper of the delivery queries of one key (section 4.15, D289). It logs each call with the event ids of the
/// delivery management: the count at the Debug level, or the error. It never logs an event, an envelope, an address, or
/// the JSON text of a content or a trace.
/// </summary>
internal sealed class DeliveryQueriesLoggingWrapper(
    IDeliveryQueries inner, TenantKey key, ILogger<DeliveryQueriesLoggingWrapper> logger, TimeProvider time) : IDeliveryQueries
{
    public static IDeliveryQueries Wrap(IServiceProvider tenantServices, TenantKey key, IDeliveryQueries inner) =>
        new DeliveryQueriesLoggingWrapper(
            inner, key, Loggers.For<DeliveryQueriesLoggingWrapper>(tenantServices), tenantServices.GetRequiredService<TimeProvider>());

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<ErrorOr<IReadOnlyList<ChannelStatus>>> GetChannelsAsync(CancellationToken ct)
    {
        var started = time.GetTimestamp();
        var result = await inner.GetChannelsAsync(ct);
        LogRead(nameof(GetChannelsAsync), result.Error, result.IsError ? 0 : result.Value.Count, started);
        return result;
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<ErrorOr<TrafficReport>> GetTrafficAsync(TrafficQuery query, CancellationToken ct)
    {
        var started = time.GetTimestamp();
        var result = await inner.GetTrafficAsync(query, ct);
        LogRead(nameof(GetTrafficAsync), result.Error, result.IsError ? 0 : result.Value.Inbox.Count + result.Value.Outbox.Count, started);
        return result;
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<ErrorOr<MessagePage<ReceivedMessageSummary>>> GetReceivedMessagesAsync(InboxQuery query, CancellationToken ct)
    {
        var started = time.GetTimestamp();
        var result = await inner.GetReceivedMessagesAsync(query, ct);
        LogRead(nameof(GetReceivedMessagesAsync), result.Error, result.IsError ? 0 : result.Value.Items.Count, started);
        return result;
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<ErrorOr<MessagePage<OutgoingMessageSummary>>> GetOutgoingMessagesAsync(OutboxQuery query, CancellationToken ct)
    {
        var started = time.GetTimestamp();
        var result = await inner.GetOutgoingMessagesAsync(query, ct);
        LogRead(nameof(GetOutgoingMessagesAsync), result.Error, result.IsError ? 0 : result.Value.Items.Count, started);
        return result;
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<ErrorOr<OutgoingMessageDetail>> GetOutgoingMessageAsync(Guid id, CancellationToken ct)
    {
        var started = time.GetTimestamp();
        var result = await inner.GetOutgoingMessageAsync(id, ct);
        LogRead(nameof(GetOutgoingMessageAsync), result.Error, result.IsError ? 0 : 1, started);
        return result;
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<ErrorOr<MessageContent>> GetReceivedContentAsync(Guid id, CancellationToken ct)
    {
        var started = time.GetTimestamp();
        var result = await inner.GetReceivedContentAsync(id, ct);
        LogRead(nameof(GetReceivedContentAsync), result.Error, result.IsError ? 0 : 1, started);
        return result;
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<ErrorOr<MessageContent>> GetOutgoingContentAsync(Guid id, CancellationToken ct)
    {
        var started = time.GetTimestamp();
        var result = await inner.GetOutgoingContentAsync(id, ct);
        LogRead(nameof(GetOutgoingContentAsync), result.Error, result.IsError ? 0 : 1, started);
        return result;
    }

    private void LogRead(string operation, Error? error, int count, long started)
    {
        var elapsedMs = time.GetElapsedTime(started).TotalMilliseconds;
        if (error is null)
        {
            logger.ReadSucceeded(operation, key.Tenant?.Value, count, elapsedMs);
        }
        else
        {
            var level = ErrorLogLevels.For(error);
            logger.OperationFailed(level, operation, key.Tenant?.Value, elapsedMs, error.Type.Code, error.Type.Name, error.Message);
        }
    }
}
