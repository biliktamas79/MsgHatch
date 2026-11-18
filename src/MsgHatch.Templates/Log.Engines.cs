using Microsoft.Extensions.Logging;

namespace MsgHatch.Templates;

/// <summary>The log events of the template engines, with event ids of the email area 2000 to 2099 (section 4.16).</summary>
internal static partial class Log
{
    [LoggerMessage(
        EventId = 2002, Level = LogLevel.Debug,
        Message = "Template engine of tenant {Tenant} prepared the {Part} template of {MessageType} in {ElapsedMs} ms")]
    public static partial void PrepareSucceeded(this ILogger logger, string messageType, string part, string? tenant, double elapsedMs);

    [LoggerMessage(
        EventId = 2003,
        Message = "Template engine of tenant {Tenant} failed for the {Part} template of {MessageType} after {ElapsedMs} ms: {ErrorCode} {ErrorName} {ErrorMessage}")]
    public static partial void PrepareFailed(
        this ILogger logger, LogLevel level, string messageType, string part, string? tenant, double elapsedMs, int errorCode, string errorName,
        string? errorMessage);

    [LoggerMessage(
        EventId = 2008, Level = LogLevel.Debug,
        Message = "Template engine of tenant {Tenant} checked a {Part} text of {MessageType} in {ElapsedMs} ms")]
    public static partial void CheckSucceeded(this ILogger logger, string messageType, string part, string? tenant, double elapsedMs);

    [LoggerMessage(
        EventId = 2009,
        Message = "Template engine of tenant {Tenant} rejected a {Part} text of {MessageType} after {ElapsedMs} ms: {ErrorCode} {ErrorName} {ErrorMessage}")]
    public static partial void CheckFailed(
        this ILogger logger, LogLevel level, string messageType, string part, string? tenant, double elapsedMs, int errorCode, string errorName,
        string? errorMessage);

    [LoggerMessage(
        EventId = 2016, Level = LogLevel.Information,
        Message = "Template engine of tenant {Tenant} found {IssueCount} issues in a {Part} text of {MessageType} in {ElapsedMs} ms")]
    public static partial void CheckFoundIssues(this ILogger logger, string messageType, string part, int issueCount, string? tenant, double elapsedMs);
}
