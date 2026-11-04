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
            Log.GetChannelsFailed(logger, level, Tenant, elapsedMs, result.Error.Type.Code, result.Error.Type.Name, result.Error.Message);
        }
        else
        {
            Log.GetChannelsSucceeded(logger, result.Value.Count, Tenant, elapsedMs);
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
            Log.GetMessageTypesFailed(logger, level, Tenant, elapsedMs, result.Error.Type.Code, result.Error.Type.Name, result.Error.Message);
        }
        else
        {
            Log.GetMessageTypesSucceeded(logger, result.Value.Count, Tenant, elapsedMs);
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
            Log.GetSubscriptionsFailed(logger, level, Tenant, elapsedMs, result.Error.Type.Code, result.Error.Type.Name, result.Error.Message);
        }
        else
        {
            Log.GetSubscriptionsSucceeded(logger, result.Value.Count, Tenant, elapsedMs);
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
            Log.GetSubscriptionFailed(
                logger, level, id, Tenant, elapsedMs, result.Error.Type.Code, result.Error.Type.Name, result.Error.Message);
        }
        else
        {
            Log.GetSubscriptionSucceeded(logger, id, Tenant, elapsedMs);
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
            Log.CreateFailed(
                logger, level, draft.Type.Name, draft.Owner.Level, Tenant, elapsedMs, result.Error.Type.Code, result.Error.Type.Name,
                result.Error.Message);
        }
        else
        {
            Log.CreateSucceeded(logger, result.Value.Id, draft.Type.Name, draft.Owner.Level, Tenant, elapsedMs);
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
            Log.UpdateFailed(
                logger, level, id, expectedRevision, Tenant, elapsedMs, result.Error.Type.Code, result.Error.Type.Name, result.Error.Message);
        }
        else
        {
            Log.UpdateSucceeded(logger, id, result.Value.Revision, Tenant, elapsedMs);
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
            Log.DeleteSucceeded(logger, id, Tenant, elapsedMs);
        }
        else
        {
            var level = ErrorLogLevels.For(error);
            Log.DeleteFailed(logger, level, id, Tenant, elapsedMs, error.Type.Code, error.Type.Name, error.Message);
        }

        return error;
    }
}
