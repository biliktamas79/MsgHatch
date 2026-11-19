using Microsoft.Extensions.Logging;

namespace MsgHatch.Domain.Management;

/// <summary>The log events of the registry queries and commands, with the event ids 2909 to 2918 of the registry area (section 4.16).</summary>
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

    [LoggerMessage(
        EventId = 2913, Level = LogLevel.Debug,
        Message = "Registry queries of tenant {Tenant} listed {Count} state rows of the channel {Channel} in {ElapsedMs} ms")]
    public static partial void GetChannelHistorySucceeded(this ILogger logger, int count, string channel, string? tenant, double elapsedMs);

    [LoggerMessage(
        EventId = 2914,
        Message = "Registry queries of tenant {Tenant} failed to list the state rows of the channel {Channel} after {ElapsedMs} ms: {ErrorCode} {ErrorName} {ErrorMessage}")]
    public static partial void GetChannelHistoryFailed(
        this ILogger logger, LogLevel level, string channel, string? tenant, double elapsedMs, int errorCode, string errorName,
        string? errorMessage);

    [LoggerMessage(
        EventId = 2915, Level = LogLevel.Information,
        Message = "Registry commands of tenant {Tenant} checked the channel {Channel} in {ElapsedMs} ms: check error {CheckErrorName}, changed {Changed}")]
    public static partial void CheckChannelSucceeded(
        this ILogger logger, string channel, string? checkErrorName, bool changed, string? tenant, double elapsedMs);

    [LoggerMessage(
        EventId = 2916,
        Message = "Registry commands of tenant {Tenant} failed to check the channel {Channel} after {ElapsedMs} ms: {ErrorCode} {ErrorName} {ErrorMessage}")]
    public static partial void CheckChannelFailed(
        this ILogger logger, LogLevel level, string channel, string? tenant, double elapsedMs, int errorCode, string errorName,
        string? errorMessage);

    [LoggerMessage(
        EventId = 2917, Level = LogLevel.Information,
        Message = "Registry commands of tenant {Tenant} completed {Operation} of {Entry} with the revision {Revision} in {ElapsedMs} ms")]
    public static partial void ChangeEntrySucceeded(
        this ILogger logger, string operation, string entry, int? revision, string? tenant, double elapsedMs);

    [LoggerMessage(
        EventId = 2918,
        Message = "Registry commands of tenant {Tenant} failed {Operation} of {Entry} with the expected revision {Revision} after {ElapsedMs} ms: {ErrorCode} {ErrorName} {ErrorMessage}")]
    public static partial void ChangeEntryFailed(
        this ILogger logger, LogLevel level, string operation, string entry, int? revision, string? tenant, double elapsedMs, int errorCode,
        string errorName, string? errorMessage);
}
