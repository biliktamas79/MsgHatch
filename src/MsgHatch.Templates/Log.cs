using Microsoft.Extensions.Logging;

namespace MsgHatch.Templates;

/// <summary>The log events of the template stores, with event ids of the email area 2000 to 2099 (section 4.16).</summary>
internal static partial class Log
{
    [LoggerMessage(
        EventId = 2000, Level = LogLevel.Debug,
        Message = "Template store of tenant {Tenant} looked up the {Part} template of {MessageType} in {ElapsedMs} ms, found: {Found}")]
    public static partial void FindSucceeded(this ILogger logger, string messageType, string part, bool found, string? tenant, double elapsedMs);

    [LoggerMessage(
        EventId = 2001,
        Message = "Template store of tenant {Tenant} failed for the {Part} template of {MessageType} after {ElapsedMs} ms: {ErrorCode} {ErrorName} {ErrorMessage}")]
    public static partial void FindFailed(
        this ILogger logger, LogLevel level, string messageType, string part, string? tenant, double elapsedMs, int errorCode, string errorName,
        string? errorMessage);

    [LoggerMessage(
        EventId = 2010, Level = LogLevel.Information,
        Message = "Template store of tenant {Tenant} saved the {Language} {Part} template of {MessageType} as version {Version} in {ElapsedMs} ms")]
    public static partial void SaveSucceeded(
        this ILogger logger, string messageType, string language, string part, long version, string? tenant, double elapsedMs);

    [LoggerMessage(
        EventId = 2011,
        Message = "Template store of tenant {Tenant} failed to save the {Language} {Part} template of {MessageType} after {ElapsedMs} ms: {ErrorCode} {ErrorName} {ErrorMessage}")]
    public static partial void SaveFailed(
        this ILogger logger, LogLevel level, string messageType, string language, string part, string? tenant, double elapsedMs, int errorCode,
        string errorName, string? errorMessage);

    [LoggerMessage(
        EventId = 2012, Level = LogLevel.Information,
        Message = "Template store of tenant {Tenant} deleted the {Language} {Part} template of {MessageType} in {ElapsedMs} ms")]
    public static partial void DeleteSucceeded(this ILogger logger, string messageType, string language, string part, string? tenant, double elapsedMs);

    [LoggerMessage(
        EventId = 2013,
        Message = "Template store of tenant {Tenant} failed to delete the {Language} {Part} template of {MessageType} after {ElapsedMs} ms: {ErrorCode} {ErrorName} {ErrorMessage}")]
    public static partial void DeleteFailed(
        this ILogger logger, LogLevel level, string messageType, string language, string part, string? tenant, double elapsedMs, int errorCode,
        string errorName, string? errorMessage);

    [LoggerMessage(
        EventId = 2014, Level = LogLevel.Debug,
        Message = "Template store of tenant {Tenant} listed {Count} templates in {ElapsedMs} ms")]
    public static partial void ListSucceeded(this ILogger logger, int count, string? tenant, double elapsedMs);

    [LoggerMessage(
        EventId = 2015,
        Message = "Template store of tenant {Tenant} failed to list the templates after {ElapsedMs} ms: {ErrorCode} {ErrorName} {ErrorMessage}")]
    public static partial void ListFailed(
        this ILogger logger, LogLevel level, string? tenant, double elapsedMs, int errorCode, string errorName, string? errorMessage);
}
