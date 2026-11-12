using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;

namespace MsgHatch.Subscriptions.Verification;

/// <summary>
/// The logging wrapper of the routing verifier (section 4.15). It logs the tenant of its key and the counts of the report,
/// and never a row, a user id, or an address (D37).
/// </summary>
internal sealed class RoutingVerifierLoggingWrapper(
    IRoutingVerifier inner, TenantKey key, ILogger<RoutingVerifierLoggingWrapper> logger, TimeProvider time)
    : IRoutingVerifier
{
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<ErrorOr<RoutingVerificationReport>> VerifyAsync(RoutingVerificationRequest request, CancellationToken ct)
    {
        var started = time.GetTimestamp();
        var result = await inner.VerifyAsync(request, ct);
        var elapsedMs = time.GetElapsedTime(started).TotalMilliseconds;
        if (result.IsError)
        {
            var level = ErrorLogLevels.For(result.Error);
            logger.VerifyFailed(level, key.Tenant?.Value, elapsedMs, result.Error.Type.Code, result.Error.Type.Name, result.Error.Message);
        }
        else
        {
            var report = result.Value;
            logger.VerifySucceeded(report.Checks.Count, report.Passed, report.Failed, report.Skipped, key.Tenant?.Value, elapsedMs);
        }

        return result;
    }
}
