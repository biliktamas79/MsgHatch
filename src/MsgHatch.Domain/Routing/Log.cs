using Microsoft.Extensions.Logging;

namespace MsgHatch.Domain.Routing;

/// <summary>The log events of the registry administration, with the event ids 2900 to 2999 (section 4.16).</summary>
internal static partial class Log
{
    [LoggerMessage(
        EventId = 2900, Level = LogLevel.Debug,
        Message = "Registry settings store GetEntriesAsync gave {ChannelCount} channel entries and {MessageTypeCount} message type entries in {ElapsedMs} ms")]
    public static partial void StoreReadSucceeded(this ILogger logger, int channelCount, int messageTypeCount, double elapsedMs);

    [LoggerMessage(
        EventId = 2901,
        Message = "Registry settings store GetEntriesAsync failed after {ElapsedMs} ms: {ErrorCode} {ErrorName} {ErrorMessage}")]
    public static partial void StoreReadFailed(
        this ILogger logger, LogLevel level, double elapsedMs, int errorCode, string errorName, string? errorMessage);

    [LoggerMessage(
        EventId = 2902, Level = LogLevel.Debug,
        Message = "Registry settings store {Operation} of {Entry} with the revision {Revision} completed in {ElapsedMs} ms")]
    public static partial void StoreChangeSucceeded(this ILogger logger, string operation, string entry, int? revision, double elapsedMs);

    [LoggerMessage(
        EventId = 2903,
        Message = "Registry settings store {Operation} of {Entry} with the revision {Revision} failed after {ElapsedMs} ms: {ErrorCode} {ErrorName} {ErrorMessage}")]
    public static partial void StoreChangeFailed(
        this ILogger logger, LogLevel level, string operation, string entry, int? revision, double elapsedMs, int errorCode,
        string errorName, string? errorMessage);

    [LoggerMessage(
        EventId = 2904, Level = LogLevel.Debug,
        Message = "Registry GetEntriesAsync gave {ChannelCount} channel entries and {MessageTypeCount} message type entries in {ElapsedMs} ms")]
    public static partial void WriterReadSucceeded(this ILogger logger, int channelCount, int messageTypeCount, double elapsedMs);

    [LoggerMessage(
        EventId = 2905,
        Message = "Registry GetEntriesAsync failed after {ElapsedMs} ms: {ErrorCode} {ErrorName} {ErrorMessage}")]
    public static partial void WriterReadFailed(
        this ILogger logger, LogLevel level, double elapsedMs, int errorCode, string errorName, string? errorMessage);

    [LoggerMessage(
        EventId = 2906, Level = LogLevel.Information,
        Message = "Registry {Operation} of {Entry} completed with the revision {Revision} in {ElapsedMs} ms")]
    public static partial void WriterChangeSucceeded(this ILogger logger, string operation, string entry, int? revision, double elapsedMs);

    [LoggerMessage(
        EventId = 2907,
        Message = "Registry {Operation} of {Entry} with the expected revision {Revision} failed after {ElapsedMs} ms: {ErrorCode} {ErrorName} {ErrorMessage}")]
    public static partial void WriterChangeFailed(
        this ILogger logger, LogLevel level, string operation, string entry, int? revision, double elapsedMs, int errorCode,
        string errorName, string? errorMessage);

    [LoggerMessage(
        EventId = 2908, Level = LogLevel.Warning,
        Message = "Registry read of the settings store failed, so the registry keeps its last snapshot: {ErrorCode} {ErrorName} {ErrorMessage}")]
    public static partial void RefreshFailed(this ILogger logger, int errorCode, string errorName, string? errorMessage);
}
