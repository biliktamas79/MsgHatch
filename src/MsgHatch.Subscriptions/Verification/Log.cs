using Microsoft.Extensions.Logging;

namespace MsgHatch.Subscriptions.Verification;

/// <summary>
/// The log events of the routing verifier, with event ids of the subscriptions area 1100 to 1199 (section 4.16). No event
/// holds a row, a user id, a company id, or an address (D37).
/// </summary>
internal static partial class Log
{
    [LoggerMessage(
        EventId = 1136, Level = LogLevel.Debug,
        Message = "Routing verifier gave {CheckCount} checks: {PassedCount} passed, {FailedCount} failed, and {SkippedCount} skipped, of tenant {Tenant} in {ElapsedMs} ms")]
    public static partial void VerifySucceeded(
        this ILogger logger, int checkCount, int passedCount, int failedCount, int skippedCount, string? tenant, double elapsedMs);

    [LoggerMessage(
        EventId = 1137,
        Message = "Routing verification of tenant {Tenant} failed after {ElapsedMs} ms: {ErrorCode} {ErrorName} {ErrorMessage}")]
    public static partial void VerifyFailed(
        this ILogger logger, LogLevel level, string? tenant, double elapsedMs, int errorCode, string errorName, string? errorMessage);
}
