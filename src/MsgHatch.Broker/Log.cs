using Microsoft.Extensions.Logging;

namespace MsgHatch.Broker;

/// <summary>The log events of the broker clients, with event ids of the broker area 2300 to 2399 (section 4.16).</summary>
internal static partial class Log
{
    [LoggerMessage(
        EventId = 2300, Level = LogLevel.Debug,
        Message = "Broker client published outgoing message {OutgoingMessageId} in {ElapsedMs} ms")]
    public static partial void PublishSucceeded(ILogger logger, Guid outgoingMessageId, double elapsedMs);

    [LoggerMessage(
        EventId = 2301,
        Message = "Broker client publish of outgoing message {OutgoingMessageId} failed after {ElapsedMs} ms: {ErrorCode} {ErrorName} {ErrorMessage}")]
    public static partial void PublishFailed(
        ILogger logger, LogLevel level, Guid outgoingMessageId, double elapsedMs, int errorCode, string errorName, string? errorMessage);

    [LoggerMessage(
        EventId = 2302, Level = LogLevel.Debug,
        Message = "Broker client health check succeeded in {ElapsedMs} ms")]
    public static partial void CheckHealthSucceeded(ILogger logger, double elapsedMs);

    [LoggerMessage(
        EventId = 2303,
        Message = "Broker client health check failed after {ElapsedMs} ms: {ErrorCode} {ErrorName} {ErrorMessage}")]
    public static partial void CheckHealthFailed(
        ILogger logger, LogLevel level, double elapsedMs, int errorCode, string errorName, string? errorMessage);
}
