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
}
