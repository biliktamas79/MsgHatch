using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;

namespace MsgHatch.Subscriptions;

/// <summary>
/// The logging wrapper of the recipient directory (section 4.15). It logs the tenant of its key and counts, and never a
/// user id, a company id, a language, or an address of a profile (D37).
/// </summary>
internal sealed class RecipientDirectoryLoggingWrapper(
    IRecipientDirectory inner, TenantKey key, ILogger<RecipientDirectoryLoggingWrapper> logger, TimeProvider time)
    : IRecipientDirectory
{
    private string? Tenant => key.Tenant?.Value;

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<ErrorOr<IReadOnlyList<UserProfile>>> GetUsersAsync(IReadOnlyList<UserId> users, CancellationToken ct)
    {
        var started = time.GetTimestamp();
        var result = await inner.GetUsersAsync(users, ct);
        var elapsedMs = time.GetElapsedTime(started).TotalMilliseconds;
        if (result.IsError)
        {
            var level = ErrorLogLevels.For(result.Error);
            Log.GetUsersFailed(
                logger, level, users.Count, Tenant, elapsedMs, result.Error.Type.Code,
                result.Error.Type.Name, result.Error.Message);
        }
        else
        {
            Log.GetUsersSucceeded(logger, result.Value.Count, users.Count, Tenant, elapsedMs);
        }

        return result;
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<ErrorOr<IReadOnlyList<UserProfile>>> GetCompanyUsersAsync(CompanyId company, CancellationToken ct)
    {
        var started = time.GetTimestamp();
        var result = await inner.GetCompanyUsersAsync(company, ct);
        var elapsedMs = time.GetElapsedTime(started).TotalMilliseconds;
        if (result.IsError)
        {
            var level = ErrorLogLevels.For(result.Error);
            Log.GetCompanyUsersFailed(
                logger, level, Tenant, elapsedMs, result.Error.Type.Code, result.Error.Type.Name,
                result.Error.Message);
        }
        else
        {
            Log.GetCompanyUsersSucceeded(logger, result.Value.Count, Tenant, elapsedMs);
        }

        return result;
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<ErrorOr<CompanyProfile?>> GetCompanyAsync(CompanyId company, CancellationToken ct)
    {
        var started = time.GetTimestamp();
        var result = await inner.GetCompanyAsync(company, ct);
        var elapsedMs = time.GetElapsedTime(started).TotalMilliseconds;
        if (result.IsError)
        {
            var level = ErrorLogLevels.For(result.Error);
            Log.GetCompanyFailed(
                logger, level, Tenant, elapsedMs, result.Error.Type.Code, result.Error.Type.Name,
                result.Error.Message);
        }
        else
        {
            Log.GetCompanySucceeded(logger, Tenant, result.Value is not null, elapsedMs);
        }

        return result;
    }
}
