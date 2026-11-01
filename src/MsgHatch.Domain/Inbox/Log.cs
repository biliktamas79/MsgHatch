using Microsoft.Extensions.Logging;
using MsgHatch.Domain.Delivery;

namespace MsgHatch.Domain.Inbox;

/// <summary>The log events of the inbox, with the event ids 1200 to 1299 (section 4.16).</summary>
internal static partial class Log
{
    [LoggerMessage(
        EventId = 1200, Level = LogLevel.Debug,
        Message = "Inbox AddAsync stored message {MessageId} of {MessageType} of tenant {Tenant} with flags {MessageFlags} in {ElapsedMs} ms")]
    public static partial void AddSucceeded(
        ILogger logger, Guid messageId, string messageType, string? tenant, MessageFlags messageFlags, double elapsedMs);

    [LoggerMessage(
        EventId = 1201,
        Message = "Inbox AddAsync of message {MessageId} of {MessageType} of tenant {Tenant} with flags {MessageFlags} failed after {ElapsedMs} ms: {ErrorCode} {ErrorName} {ErrorMessage}")]
    public static partial void AddFailed(
        ILogger logger, LogLevel level, Guid messageId, string messageType, string? tenant, MessageFlags messageFlags,
        double elapsedMs, int errorCode, string errorName, string? errorMessage);

    [LoggerMessage(
        EventId = 1202, Level = LogLevel.Debug,
        Message = "Inbox ClaimDueAsync claimed {Count} messages in {ElapsedMs} ms")]
    public static partial void ClaimSucceeded(ILogger logger, int count, double elapsedMs);

    [LoggerMessage(
        EventId = 1203,
        Message = "Inbox ClaimDueAsync failed after {ElapsedMs} ms: {ErrorCode} {ErrorName} {ErrorMessage}")]
    public static partial void ClaimFailed(
        ILogger logger, LogLevel level, double elapsedMs, int errorCode, string errorName, string? errorMessage);

    [LoggerMessage(
        EventId = 1204, Level = LogLevel.Debug,
        Message = "Inbox {Operation} recorded message {MessageId} of {MessageType} of tenant {Tenant} with flags {MessageFlags} in {ElapsedMs} ms")]
    public static partial void RecordSucceeded(
        ILogger logger, string operation, Guid messageId, string messageType, string? tenant, MessageFlags messageFlags,
        double elapsedMs);

    [LoggerMessage(
        EventId = 1205,
        Message = "Inbox {Operation} of message {MessageId} of {MessageType} of tenant {Tenant} with flags {MessageFlags} failed after {ElapsedMs} ms: {ErrorCode} {ErrorName} {ErrorMessage}")]
    public static partial void RecordFailed(
        ILogger logger, LogLevel level, string operation, Guid messageId, string messageType, string? tenant,
        MessageFlags messageFlags, double elapsedMs, int errorCode, string errorName, string? errorMessage);

    [LoggerMessage(
        EventId = 1206, Level = LogLevel.Debug,
        Message = "Inbox DeleteTestDataAsync deleted {Count} messages in {ElapsedMs} ms")]
    public static partial void DeleteTestDataSucceeded(ILogger logger, int count, double elapsedMs);

    [LoggerMessage(
        EventId = 1207,
        Message = "Inbox DeleteTestDataAsync failed after {ElapsedMs} ms: {ErrorCode} {ErrorName} {ErrorMessage}")]
    public static partial void DeleteTestDataFailed(
        ILogger logger, LogLevel level, double elapsedMs, int errorCode, string errorName, string? errorMessage);
}
