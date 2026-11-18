using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;

namespace MsgHatch.Subscriptions.Management;

/// <summary>
/// The logging wrapper of the subscription commands (section 4.15). It logs the tenant of its key, ids, revisions,
/// message types, and levels, and never a user id, a company id, or channel settings (D37).
/// </summary>
internal sealed class SubscriptionCommandsLoggingWrapper(
    ISubscriptionCommands inner, TenantKey key, ILogger<SubscriptionCommandsLoggingWrapper> logger, TimeProvider time)
    : ISubscriptionCommands
{
    private string? Tenant => key.Tenant?.Value;

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
