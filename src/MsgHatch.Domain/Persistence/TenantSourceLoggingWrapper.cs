using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;

namespace MsgHatch.Domain.Persistence;

/// <summary>The logging wrapper of the tenant source (section 4.15). It logs the number of tenants, and no tenant id.</summary>
internal sealed class TenantSourceLoggingWrapper(
    ITenantSource inner, ILogger<TenantSourceLoggingWrapper> logger, TimeProvider time) : ITenantSource
{
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<ErrorOr<IReadOnlyList<TenantId>>> GetTenantsAsync(CancellationToken ct)
    {
        var started = time.GetTimestamp();
        var result = await inner.GetTenantsAsync(ct);
        var elapsedMs = time.GetElapsedTime(started).TotalMilliseconds;
        if (result.IsError)
        {
            var level = ErrorLogLevels.For(result.Error);
            logger.TenantsReadFailed(level, elapsedMs, result.Error.Type.Code, result.Error.Type.Name, result.Error.Message);
        }
        else
        {
            logger.TenantsRead(result.Value.Count, elapsedMs);
        }

        return result;
    }
}
