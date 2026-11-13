using Microsoft.Extensions.Logging;

namespace MsgHatch.Domain.Persistence;

/// <summary>The log events of the persistence, with the event ids 1500 to 1599 (section 4.16).</summary>
internal static partial class Log
{
    [LoggerMessage(
        EventId = 1500, Level = LogLevel.Debug,
        Message = "Tenant source GetTenantsAsync gave {TenantCount} tenants in {ElapsedMs} ms")]
    public static partial void TenantsRead(this ILogger logger, int tenantCount, double elapsedMs);

    [LoggerMessage(
        EventId = 1501,
        Message = "Tenant source GetTenantsAsync failed after {ElapsedMs} ms: {ErrorCode} {ErrorName} {ErrorMessage}")]
    public static partial void TenantsReadFailed(
        this ILogger logger, LogLevel level, double elapsedMs, int errorCode, string errorName, string? errorMessage);

    [LoggerMessage(
        EventId = 1502,
        Message = "Claim of the store of the tenant {TenantId} for {Channel} failed, and the claim skips it: {ErrorCode} {ErrorName} {ErrorMessage}")]
    public static partial void TenantClaimFailed(
        this ILogger logger, LogLevel level, string tenantId, string? channel, int errorCode, string errorName, string? errorMessage);
}
