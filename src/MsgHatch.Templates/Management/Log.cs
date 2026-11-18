using Microsoft.Extensions.Logging;

namespace MsgHatch.Templates.Management;

/// <summary>
/// The log events of the template queries and commands, with event ids of the email area 2000 to 2099 (section 4.16). No
/// event holds a template text, a company id, or the message of an issue (D37).
/// </summary>
internal static partial class Log
{
    [LoggerMessage(
        EventId = 2017, Level = LogLevel.Debug,
        Message = "Template queries of tenant {Tenant} listed {Count} templates in {ElapsedMs} ms")]
    public static partial void GetTemplatesSucceeded(this ILogger logger, int count, string? tenant, double elapsedMs);

    [LoggerMessage(
        EventId = 2018,
        Message = "Template queries of tenant {Tenant} failed to list the templates after {ElapsedMs} ms: {ErrorCode} {ErrorName} {ErrorMessage}")]
    public static partial void GetTemplatesFailed(
        this ILogger logger, LogLevel level, string? tenant, double elapsedMs, int errorCode, string errorName, string? errorMessage);

    [LoggerMessage(
        EventId = 2019, Level = LogLevel.Debug,
        Message = "Template queries of tenant {Tenant} read the {Language} {Part} template of {MessageType} in {ElapsedMs} ms")]
    public static partial void GetTemplateSucceeded(this ILogger logger, string messageType, string language, string part, string? tenant, double elapsedMs);

    [LoggerMessage(
        EventId = 2020,
        Message = "Template queries of tenant {Tenant} failed to read the {Language} {Part} template of {MessageType} after {ElapsedMs} ms: {ErrorCode} {ErrorName} {ErrorMessage}")]
    public static partial void GetTemplateFailed(
        this ILogger logger, LogLevel level, string messageType, string language, string part, string? tenant, double elapsedMs, int errorCode,
        string errorName, string? errorMessage);

    [LoggerMessage(
        EventId = 2021, Level = LogLevel.Debug,
        Message = "Template queries of tenant {Tenant} listed {Count} declared parts in {ElapsedMs} ms")]
    public static partial void GetTemplatePartsSucceeded(this ILogger logger, int count, string? tenant, double elapsedMs);

    [LoggerMessage(
        EventId = 2022,
        Message = "Template queries of tenant {Tenant} failed to list the declared parts after {ElapsedMs} ms: {ErrorCode} {ErrorName} {ErrorMessage}")]
    public static partial void GetTemplatePartsFailed(
        this ILogger logger, LogLevel level, string? tenant, double elapsedMs, int errorCode, string errorName, string? errorMessage);

    [LoggerMessage(
        EventId = 2023, Level = LogLevel.Debug,
        Message = "Template queries of tenant {Tenant} checked a {Language} {Part} draft of {MessageType} with {IssueCount} issues in {ElapsedMs} ms")]
    public static partial void CheckTemplateSucceeded(
        this ILogger logger, string messageType, string language, string part, int issueCount, string? tenant, double elapsedMs);

    [LoggerMessage(
        EventId = 2024,
        Message = "Template queries of tenant {Tenant} failed to check a {Language} {Part} draft of {MessageType} after {ElapsedMs} ms: {ErrorCode} {ErrorName} {ErrorMessage}")]
    public static partial void CheckTemplateFailed(
        this ILogger logger, LogLevel level, string messageType, string language, string part, string? tenant, double elapsedMs, int errorCode,
        string errorName, string? errorMessage);

    [LoggerMessage(
        EventId = 2025, Level = LogLevel.Debug,
        Message = "Template commands of tenant {Tenant} saved the {Language} {Part} template of {MessageType} as version {Version} in {ElapsedMs} ms")]
    public static partial void SaveTemplateSucceeded(
        this ILogger logger, string messageType, string language, string part, long version, string? tenant, double elapsedMs);

    [LoggerMessage(
        EventId = 2026,
        Message = "Template commands of tenant {Tenant} failed to save the {Language} {Part} template of {MessageType} after {ElapsedMs} ms: {ErrorCode} {ErrorName} {ErrorMessage}")]
    public static partial void SaveTemplateFailed(
        this ILogger logger, LogLevel level, string messageType, string language, string part, string? tenant, double elapsedMs, int errorCode,
        string errorName, string? errorMessage);

    [LoggerMessage(
        EventId = 2027, Level = LogLevel.Debug,
        Message = "Template commands of tenant {Tenant} deleted the {Language} {Part} template of {MessageType} in {ElapsedMs} ms")]
    public static partial void DeleteTemplateSucceeded(this ILogger logger, string messageType, string language, string part, string? tenant, double elapsedMs);

    [LoggerMessage(
        EventId = 2028,
        Message = "Template commands of tenant {Tenant} failed to delete the {Language} {Part} template of {MessageType} after {ElapsedMs} ms: {ErrorCode} {ErrorName} {ErrorMessage}")]
    public static partial void DeleteTemplateFailed(
        this ILogger logger, LogLevel level, string messageType, string language, string part, string? tenant, double elapsedMs, int errorCode,
        string errorName, string? errorMessage);
}
