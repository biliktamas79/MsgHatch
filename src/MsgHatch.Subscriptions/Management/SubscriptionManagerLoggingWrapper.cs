using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Routing;

namespace MsgHatch.Subscriptions.Management;

/// <summary>
/// The logging wrapper of the subscription manager (section 4.15). It logs the tenant of its key, ids, message types,
/// levels, and counts, and never a user id, a company id, or channel settings (D37).
/// </summary>
internal sealed class SubscriptionManagerLoggingWrapper(
    ISubscriptionManager inner, TenantKey key, ILogger<SubscriptionManagerLoggingWrapper> logger, TimeProvider time)
    : ISubscriptionManager
{
    private string? Tenant => key.Tenant?.Value;

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<ErrorOr<IReadOnlyList<ChannelDescriptor>>> GetChannelsAsync(CancellationToken ct)
    {
        var started = time.GetTimestamp();
        var result = await inner.GetChannelsAsync(ct);
        var elapsedMs = time.GetElapsedTime(started).TotalMilliseconds;
        if (result.IsError)
        {
            var level = ErrorLogLevels.For(result.Error);
            logger.GetChannelsFailed(level, Tenant, elapsedMs, result.Error.Type.Code, result.Error.Type.Name, result.Error.Message);
        }
        else
        {
            logger.GetChannelsSucceeded(result.Value.Count, Tenant, elapsedMs);
        }

        return result;
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<ErrorOr<IReadOnlyList<MessageTypeState>>> GetMessageTypesAsync(MessageTypeQuery query, CancellationToken ct)
    {
        var started = time.GetTimestamp();
        var result = await inner.GetMessageTypesAsync(query, ct);
        var elapsedMs = time.GetElapsedTime(started).TotalMilliseconds;
        if (result.IsError)
        {
            var level = ErrorLogLevels.For(result.Error);
            logger.GetMessageTypesFailed(level, Tenant, elapsedMs, result.Error.Type.Code, result.Error.Type.Name, result.Error.Message);
        }
        else
        {
            logger.GetMessageTypesSucceeded(result.Value.Count, Tenant, elapsedMs);
        }

        return result;
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<ErrorOr<IReadOnlyList<Subscription>>> GetSubscriptionsAsync(SubscriptionQuery query, CancellationToken ct)
    {
        var started = time.GetTimestamp();
        var result = await inner.GetSubscriptionsAsync(query, ct);
        var elapsedMs = time.GetElapsedTime(started).TotalMilliseconds;
        if (result.IsError)
        {
            var level = ErrorLogLevels.For(result.Error);
            logger.GetSubscriptionsFailed(level, Tenant, elapsedMs, result.Error.Type.Code, result.Error.Type.Name, result.Error.Message);
        }
        else
        {
            logger.GetSubscriptionsSucceeded(result.Value.Count, Tenant, elapsedMs);
        }

        return result;
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<ErrorOr<Subscription>> GetSubscriptionAsync(Guid id, CancellationToken ct)
    {
        var started = time.GetTimestamp();
        var result = await inner.GetSubscriptionAsync(id, ct);
        var elapsedMs = time.GetElapsedTime(started).TotalMilliseconds;
        if (result.IsError)
        {
            var level = ErrorLogLevels.For(result.Error);
            logger.GetSubscriptionFailed(
                level, id, Tenant, elapsedMs, result.Error.Type.Code, result.Error.Type.Name, result.Error.Message);
        }
        else
        {
            logger.GetSubscriptionSucceeded(id, Tenant, elapsedMs);
        }

        return result;
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<ErrorOr<Subscription>> CreateAsync(SubscriptionDraft draft, CancellationToken ct)
    {
        var started = time.GetTimestamp();
        var result = await inner.CreateAsync(draft, ct);
        var elapsedMs = time.GetElapsedTime(started).TotalMilliseconds;
        if (result.IsError)
        {
            var level = ErrorLogLevels.For(result.Error);
            logger.CreateFailed(
                level, draft.Type.Name, draft.Owner.Level, Tenant, elapsedMs, result.Error.Type.Code, result.Error.Type.Name,
                result.Error.Message);
        }
        else
        {
            logger.CreateSucceeded(result.Value.Id, draft.Type.Name, draft.Owner.Level, Tenant, elapsedMs);
        }

        return result;
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<ErrorOr<Subscription>> UpdateAsync(Guid id, int expectedRevision, SubscriptionDraft draft, CancellationToken ct)
    {
        var started = time.GetTimestamp();
        var result = await inner.UpdateAsync(id, expectedRevision, draft, ct);
        var elapsedMs = time.GetElapsedTime(started).TotalMilliseconds;
        if (result.IsError)
        {
            var level = ErrorLogLevels.For(result.Error);
            logger.UpdateFailed(
                level, id, expectedRevision, Tenant, elapsedMs, result.Error.Type.Code, result.Error.Type.Name, result.Error.Message);
        }
        else
        {
            logger.UpdateSucceeded(id, result.Value.Revision, Tenant, elapsedMs);
        }

        return result;
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<Error?> DeleteAsync(Guid id, CancellationToken ct)
    {
        var started = time.GetTimestamp();
        var error = await inner.DeleteAsync(id, ct);
        var elapsedMs = time.GetElapsedTime(started).TotalMilliseconds;
        if (error is null)
        {
            logger.DeleteSucceeded(id, Tenant, elapsedMs);
        }
        else
        {
            var level = ErrorLogLevels.For(error);
            logger.DeleteFailed(level, id, Tenant, elapsedMs, error.Type.Code, error.Type.Name, error.Message);
        }

        return error;
    }
}
