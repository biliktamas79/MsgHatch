using Microsoft.Extensions.Logging;

namespace MsgHatch.Domain.Delivery;

/// <summary>
/// The log events of the transports, with the event ids 1350 to 1399 of the outbox area (section 4.16), because a
/// transport sends the messages of the outbox.
/// </summary>
internal static partial class Log
{
    [LoggerMessage(
        EventId = 1350, Level = LogLevel.Debug,
        Message = "Transport sent outgoing message {OutgoingMessageId} of {MessageType} on {Channel} of tenant {Tenant} with flags {MessageFlags} in {ElapsedMs} ms")]
    public static partial void SendSucceeded(
        ILogger logger, Guid outgoingMessageId, string messageType, string channel, string? tenant, MessageFlags messageFlags,
        double elapsedMs);

    [LoggerMessage(
        EventId = 1351,
        Message = "Transport send of outgoing message {OutgoingMessageId} of {MessageType} on {Channel} of tenant {Tenant} with flags {MessageFlags} failed after {ElapsedMs} ms: {ErrorCode} {ErrorName} {ErrorMessage}")]
    public static partial void SendFailed(
        ILogger logger, LogLevel level, Guid outgoingMessageId, string messageType, string channel, string? tenant,
        MessageFlags messageFlags, double elapsedMs, int errorCode, string errorName, string? errorMessage);

    [LoggerMessage(
        EventId = 1352, Level = LogLevel.Debug,
        Message = "Transport health check of {Channel} succeeded in {ElapsedMs} ms")]
    public static partial void CheckHealthSucceeded(ILogger logger, string channel, double elapsedMs);

    [LoggerMessage(
        EventId = 1353,
        Message = "Transport health check of {Channel} failed after {ElapsedMs} ms: {ErrorCode} {ErrorName} {ErrorMessage}")]
    public static partial void CheckHealthFailed(
        ILogger logger, LogLevel level, string channel, double elapsedMs, int errorCode, string errorName, string? errorMessage);
}
