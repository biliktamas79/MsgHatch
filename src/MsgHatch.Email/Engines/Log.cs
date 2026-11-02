using Microsoft.Extensions.Logging;

namespace MsgHatch.Email.Engines;

/// <summary>The log events of the template engines, with event ids of the email area 2000 to 2099 (section 4.16).</summary>
internal static partial class Log
{
    [LoggerMessage(
        EventId = 2002, Level = LogLevel.Debug,
        Message = "Template engine of tenant {Tenant} prepared the template of {MessageType} in {ElapsedMs} ms")]
    public static partial void PrepareSucceeded(ILogger logger, string messageType, string? tenant, double elapsedMs);

    [LoggerMessage(
        EventId = 2003,
        Message = "Template engine of tenant {Tenant} failed for {MessageType} after {ElapsedMs} ms: {ErrorCode} {ErrorName} {ErrorMessage}")]
    public static partial void PrepareFailed(
        ILogger logger, LogLevel level, string messageType, string? tenant, double elapsedMs, int errorCode, string errorName,
        string? errorMessage);
}
