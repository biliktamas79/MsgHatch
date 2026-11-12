using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;

namespace MsgHatch.Subscriptions;

/// <summary>
/// The logging wrapper of a subscription store (section 4.15). It logs the tenant of its key, ids, message types,
/// levels, and counts, and never a user id, a company id, or channel settings (D37).
/// </summary>
internal sealed class SubscriptionStoreLoggingWrapper(
    ISubscriptionStore inner, TenantKey key, ILogger<SubscriptionStoreLoggingWrapper> logger, TimeProvider time)
    : ISubscriptionStore
{
    private string? Tenant => key.Tenant?.Value;

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<ErrorOr<IReadOnlyList<Subscription>>> FindForDeliveryAsync(
        MessageType type, IReadOnlyList<CompanyId> companies, IReadOnlyList<UserId> users, CancellationToken ct)
    {
        var started = time.GetTimestamp();
        var result = await inner.FindForDeliveryAsync(type, companies, users, ct);
        var elapsedMs = time.GetElapsedTime(started).TotalMilliseconds;
        if (result.IsError)
        {
            var level = ErrorLogLevels.For(result.Error);
            logger.FindForDeliveryFailed(
                level, type.Name, companies.Count, users.Count, Tenant, elapsedMs,
                result.Error.Type.Code, result.Error.Type.Name, result.Error.Message);
        }
        else
        {
            logger.FindForDeliverySucceeded(result.Value.Count, type.Name, companies.Count, users.Count, Tenant, elapsedMs);
        }

        return result;
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<ErrorOr<IReadOnlyList<Subscription>>> QueryAsync(SubscriptionQuery query, CancellationToken ct)
    {
        var started = time.GetTimestamp();
        var result = await inner.QueryAsync(query, ct);
        var elapsedMs = time.GetElapsedTime(started).TotalMilliseconds;
        if (result.IsError)
        {
            var level = ErrorLogLevels.For(result.Error);
            logger.QueryFailed(
                level, Tenant, elapsedMs, result.Error.Type.Code, result.Error.Type.Name,
                result.Error.Message);
        }
        else
        {
            logger.QuerySucceeded(result.Value.Count, Tenant, elapsedMs);
        }

        return result;
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<ErrorOr<Subscription?>> GetAsync(Guid id, CancellationToken ct)
    {
        var started = time.GetTimestamp();
        var result = await inner.GetAsync(id, ct);
        var elapsedMs = time.GetElapsedTime(started).TotalMilliseconds;
        if (result.IsError)
        {
            var level = ErrorLogLevels.For(result.Error);
            logger.GetFailed(
                level, id, Tenant, elapsedMs, result.Error.Type.Code, result.Error.Type.Name,
                result.Error.Message);
        }
        else
        {
            logger.GetSucceeded(id, Tenant, result.Value is not null, elapsedMs);
        }

        return result;
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<Error?> AddAsync(Subscription subscription, CancellationToken ct)
    {
        var started = time.GetTimestamp();
        var error = await inner.AddAsync(subscription, ct);
        var elapsedMs = time.GetElapsedTime(started).TotalMilliseconds;
        if (error is null)
        {
            logger.AddSucceeded(subscription.Id, subscription.Type.Name, subscription.Owner.Level, Tenant, elapsedMs);
        }
        else
        {
            var level = ErrorLogLevels.For(error);
            logger.AddFailed(
                level, subscription.Id, subscription.Type.Name, subscription.Owner.Level, Tenant,
                elapsedMs, error.Type.Code, error.Type.Name, error.Message);
        }

        return error;
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<Error?> UpdateAsync(Subscription subscription, int expectedRevision, CancellationToken ct)
    {
        var started = time.GetTimestamp();
        var error = await inner.UpdateAsync(subscription, expectedRevision, ct);
        var elapsedMs = time.GetElapsedTime(started).TotalMilliseconds;
        if (error is null)
        {
            logger.UpdateSucceeded(subscription.Id, subscription.Revision, Tenant, elapsedMs);
        }
        else
        {
            var level = ErrorLogLevels.For(error);
            logger.UpdateFailed(
                level, subscription.Id, expectedRevision, Tenant, elapsedMs, error.Type.Code,
                error.Type.Name, error.Message);
        }

        return error;
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
            logger.DeleteFailed(
                level, id, Tenant, elapsedMs, error.Type.Code, error.Type.Name, error.Message);
        }

        return error;
    }
}
