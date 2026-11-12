using Microsoft.Extensions.Logging;

namespace MsgHatch.Subscriptions.Caching;

/// <summary>
/// The log events of the subscription caching wrapper, with event ids of the subscriptions area 1100 to 1199
/// (section 4.16). No event holds a cache key, a user id, or a company id (D37).
/// </summary>
internal static partial class Log
{
    [LoggerMessage(
        EventId = 1134, Level = LogLevel.Debug,
        Message = "Subscription cache had {HitCount} hits and {MissCount} misses of the owners of {MessageType} of tenant {Tenant}")]
    public static partial void FindForDeliveryCached(this ILogger logger, int hitCount, int missCount, string messageType, string? tenant);

    [LoggerMessage(
        EventId = 1135, Level = LogLevel.Warning,
        Message = "Subscription cache backend {Operation} of tenant {Tenant} failed: {ErrorCode} {ErrorName} {ErrorMessage}")]
    public static partial void BackendFailed(
        this ILogger logger, string operation, string? tenant, int errorCode, string errorName, string? errorMessage);
}
