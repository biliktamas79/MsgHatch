using Microsoft.Extensions.Logging;

namespace MsgHatch.Email;

/// <summary>The log events of the email clients, with event ids of the email area 2000 to 2099 (section 4.16).</summary>
internal static partial class Log
{
    [LoggerMessage(
        EventId = 2004, Level = LogLevel.Debug,
        Message = "Email client sent outgoing message {OutgoingMessageId} in {ElapsedMs} ms")]
    public static partial void SendSucceeded(ILogger logger, Guid outgoingMessageId, double elapsedMs);

    [LoggerMessage(
        EventId = 2005,
        Message = "Email client send of outgoing message {OutgoingMessageId} failed after {ElapsedMs} ms: {ErrorCode} {ErrorName} {ErrorMessage}")]
    public static partial void SendFailed(
        ILogger logger, LogLevel level, Guid outgoingMessageId, double elapsedMs, int errorCode, string errorName, string? errorMessage);

    [LoggerMessage(
        EventId = 2006, Level = LogLevel.Debug,
        Message = "Email client health check succeeded in {ElapsedMs} ms")]
    public static partial void CheckHealthSucceeded(ILogger logger, double elapsedMs);

    [LoggerMessage(
        EventId = 2007,
        Message = "Email client health check failed after {ElapsedMs} ms: {ErrorCode} {ErrorName} {ErrorMessage}")]
    public static partial void CheckHealthFailed(
        ILogger logger, LogLevel level, double elapsedMs, int errorCode, string errorName, string? errorMessage);
}
