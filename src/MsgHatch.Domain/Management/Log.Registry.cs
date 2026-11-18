using Microsoft.Extensions.Logging;

namespace MsgHatch.Domain.Management;

/// <summary>The log events of the registry queries, with the event ids 2909 to 2912 of the registry area (section 4.16).</summary>
internal static partial class Log
{
    [LoggerMessage(
        EventId = 2909, Level = LogLevel.Debug,
        Message = "Registry queries of tenant {Tenant} listed {Count} channels in {ElapsedMs} ms")]
    public static partial void GetChannelsSucceeded(this ILogger logger, int count, string? tenant, double elapsedMs);

    [LoggerMessage(
        EventId = 2910,
        Message = "Registry queries of tenant {Tenant} failed to list the channels after {ElapsedMs} ms: {ErrorCode} {ErrorName} {ErrorMessage}")]
    public static partial void GetChannelsFailed(
        this ILogger logger, LogLevel level, string? tenant, double elapsedMs, int errorCode, string errorName, string? errorMessage);

    [LoggerMessage(
        EventId = 2911, Level = LogLevel.Debug,
        Message = "Registry queries of tenant {Tenant} listed {Count} message types in {ElapsedMs} ms")]
    public static partial void GetMessageTypesSucceeded(this ILogger logger, int count, string? tenant, double elapsedMs);

    [LoggerMessage(
        EventId = 2912,
        Message = "Registry queries of tenant {Tenant} failed to list the message types after {ElapsedMs} ms: {ErrorCode} {ErrorName} {ErrorMessage}")]
    public static partial void GetMessageTypesFailed(
        this ILogger logger, LogLevel level, string? tenant, double elapsedMs, int errorCode, string errorName, string? errorMessage);
}
