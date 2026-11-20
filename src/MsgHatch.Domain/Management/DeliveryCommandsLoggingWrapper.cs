using System.Globalization;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Routing;

namespace MsgHatch.Domain.Management;

/// <summary>
/// The logging wrapper of the delivery commands of one key (section 4.15, D289). It logs each change at the Information level
/// with the action, the channel, the filters of the query, and the counts (section 4.19), with the event ids of the delivery
/// management. It never logs an event, an envelope, an address, a note, or the id of a target.
/// </summary>
internal sealed class DeliveryCommandsLoggingWrapper(
    IDeliveryCommands inner, TenantKey key, ILogger<DeliveryCommandsLoggingWrapper> logger, TimeProvider time) : IDeliveryCommands
{
    public static IDeliveryCommands Wrap(IServiceProvider tenantServices, TenantKey key, IDeliveryCommands inner) =>
        new DeliveryCommandsLoggingWrapper(
            inner, key, Loggers.For<DeliveryCommandsLoggingWrapper>(tenantServices), tenantServices.GetRequiredService<TimeProvider>());

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<Error?> PauseChannelAsync(ChannelId channel, string? note, CancellationToken ct)
    {
        var started = time.GetTimestamp();
        var error = await inner.PauseChannelAsync(channel, note, ct);
        LogChannelChange(nameof(PauseChannelAsync), channel, error, started);
        return error;
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<Error?> ResumeChannelAsync(ChannelId channel, CancellationToken ct)
    {
        var started = time.GetTimestamp();
        var error = await inner.ResumeChannelAsync(channel, ct);
        LogChannelChange(nameof(ResumeChannelAsync), channel, error, started);
        return error;
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<ErrorOr<DeliveryActionResult>> RetryReceivedAsync(InboxQuery query, CancellationToken ct)
    {
        var started = time.GetTimestamp();
        var result = await inner.RetryReceivedAsync(query, ct);
        LogMessagesChange(nameof(RetryReceivedAsync), FiltersOf(query), result, started);
        return result;
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<ErrorOr<DeliveryActionResult>> CancelReceivedAsync(InboxQuery query, CancellationToken ct)
    {
        var started = time.GetTimestamp();
        var result = await inner.CancelReceivedAsync(query, ct);
        LogMessagesChange(nameof(CancelReceivedAsync), FiltersOf(query), result, started);
        return result;
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<ErrorOr<DeliveryActionResult>> RetryOutgoingAsync(OutboxQuery query, CancellationToken ct)
    {
        var started = time.GetTimestamp();
        var result = await inner.RetryOutgoingAsync(query, ct);
        LogMessagesChange(nameof(RetryOutgoingAsync), FiltersOf(query), result, started);
        return result;
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<ErrorOr<DeliveryActionResult>> CancelOutgoingAsync(OutboxQuery query, CancellationToken ct)
    {
        var started = time.GetTimestamp();
        var result = await inner.CancelOutgoingAsync(query, ct);
        LogMessagesChange(nameof(CancelOutgoingAsync), FiltersOf(query), result, started);
        return result;
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<ErrorOr<DeletedMessages>> DeleteTestDataAsync(CancellationToken ct)
    {
        var started = time.GetTimestamp();
        var result = await inner.DeleteTestDataAsync(ct);
        LogDelete(nameof(DeleteTestDataAsync), result, started);
        return result;
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<ErrorOr<DeletedMessages>> PurgeAsync(DateTime finishedBeforeUtc, int maxCount, CancellationToken ct)
    {
        var started = time.GetTimestamp();
        var result = await inner.PurgeAsync(finishedBeforeUtc, maxCount, ct);
        LogDelete(nameof(PurgeAsync), result, started);
        return result;
    }

    private void LogChannelChange(string operation, ChannelId channel, Error? error, long started)
    {
        var elapsedMs = time.GetElapsedTime(started).TotalMilliseconds;
        if (error is null)
        {
            logger.ChannelChanged(operation, channel.Name, elapsedMs);
        }
        else
        {
            LogFailure(operation, error, elapsedMs);
        }
    }

    private void LogMessagesChange(string operation, string filters, ErrorOr<DeliveryActionResult> result, long started)
    {
        var elapsedMs = time.GetElapsedTime(started).TotalMilliseconds;
        if (result.IsError)
        {
            LogFailure(operation, result.Error!, elapsedMs);
        }
        else
        {
            logger.MessagesChanged(
                operation, key.Tenant?.Value, filters, result.Value.Matched, result.Value.Changed, elapsedMs);
        }
    }

    private void LogDelete(string operation, ErrorOr<DeletedMessages> result, long started)
    {
        var elapsedMs = time.GetElapsedTime(started).TotalMilliseconds;
        if (result.IsError)
        {
            LogFailure(operation, result.Error!, elapsedMs);
        }
        else
        {
            logger.MessagesDeleted(
                operation, result.Value.InboxMessages, result.Value.OutboxMessages, elapsedMs);
        }
    }

    private void LogFailure(string operation, Error error, double elapsedMs)
    {
        var level = ErrorLogLevels.For(error);
        logger.OperationFailed(level, operation, key.Tenant?.Value, elapsedMs, error.Type.Code, error.Type.Name, error.Message);
    }

    private static string FiltersOf(InboxQuery query) =>
        Describe(query.Ids, query.Status, query.Type, channel: null, target: null, sourceMessageId: null, query.CreatedFromUtc,
            query.CreatedToUtc, query.DueBeforeUtc, query.IncludeTest);

    private static string FiltersOf(OutboxQuery query) =>
        Describe(query.Ids, query.Status, query.Type, query.Channel, query.Target, query.SourceMessageId, query.CreatedFromUtc,
            query.CreatedToUtc, query.DueBeforeUtc, query.IncludeTest);

    /// <summary>Describes the given filters. A target gives only its kind, because its id is a user id or a company id.</summary>
    private static string Describe(
        IReadOnlyList<Guid>? ids, DeliveryStatus? status, MessageType? type, ChannelId? channel, DeliveryTarget? target,
        Guid? sourceMessageId, DateTime? createdFromUtc, DateTime? createdToUtc, DateTime? dueBeforeUtc, bool includeTest) =>
        string.Join(", ", new[]
        {
            ids is null ? null : $"ids: {ids.Count}",
            status is null ? null : $"status: {status}",
            type is null ? null : $"type: {type.Value.Name}",
            channel is null ? null : $"channel: {channel.Value.Name}",
            target is null ? null : $"target: {target.Value.Kind}",
            sourceMessageId is null ? null : $"source: {sourceMessageId:D}",
            createdFromUtc is null ? null : $"created from: {createdFromUtc.Value.ToString("O", CultureInfo.InvariantCulture)}",
            createdToUtc is null ? null : $"created to: {createdToUtc.Value.ToString("O", CultureInfo.InvariantCulture)}",
            dueBeforeUtc is null ? null : $"due before: {dueBeforeUtc.Value.ToString("O", CultureInfo.InvariantCulture)}",
            includeTest ? "include test" : null,
        }.OfType<string>());
}
