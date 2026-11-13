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
        this ILogger logger, Guid messageId, string messageType, string? tenant, MessageFlags messageFlags, double elapsedMs);

    [LoggerMessage(
        EventId = 1201,
        Message = "Inbox AddAsync of message {MessageId} of {MessageType} of tenant {Tenant} with flags {MessageFlags} failed after {ElapsedMs} ms: {ErrorCode} {ErrorName} {ErrorMessage}")]
    public static partial void AddFailed(
        this ILogger logger, LogLevel level, Guid messageId, string messageType, string? tenant, MessageFlags messageFlags,
        double elapsedMs, int errorCode, string errorName, string? errorMessage);

    [LoggerMessage(
        EventId = 1202, Level = LogLevel.Debug,
        Message = "Inbox ClaimDueAsync claimed {Count} messages in {ElapsedMs} ms")]
    public static partial void ClaimSucceeded(this ILogger logger, int count, double elapsedMs);

    [LoggerMessage(
        EventId = 1203,
        Message = "Inbox ClaimDueAsync failed after {ElapsedMs} ms: {ErrorCode} {ErrorName} {ErrorMessage}")]
    public static partial void ClaimFailed(
        this ILogger logger, LogLevel level, double elapsedMs, int errorCode, string errorName, string? errorMessage);

    [LoggerMessage(
        EventId = 1204, Level = LogLevel.Debug,
        Message = "Inbox {Operation} recorded message {MessageId} of {MessageType} of tenant {Tenant} with flags {MessageFlags} in {ElapsedMs} ms")]
    public static partial void RecordSucceeded(
        this ILogger logger, string operation, Guid messageId, string messageType, string? tenant, MessageFlags messageFlags,
        double elapsedMs);

    [LoggerMessage(
        EventId = 1205,
        Message = "Inbox {Operation} of message {MessageId} of {MessageType} of tenant {Tenant} with flags {MessageFlags} failed after {ElapsedMs} ms: {ErrorCode} {ErrorName} {ErrorMessage}")]
    public static partial void RecordFailed(
        this ILogger logger, LogLevel level, string operation, Guid messageId, string messageType, string? tenant,
        MessageFlags messageFlags, double elapsedMs, int errorCode, string errorName, string? errorMessage);

    [LoggerMessage(
        EventId = 1206, Level = LogLevel.Debug,
        Message = "Inbox DeleteTestDataAsync deleted {Count} messages in {ElapsedMs} ms")]
    public static partial void DeleteTestDataSucceeded(this ILogger logger, int count, double elapsedMs);

    [LoggerMessage(
        EventId = 1207,
        Message = "Inbox DeleteTestDataAsync failed after {ElapsedMs} ms: {ErrorCode} {ErrorName} {ErrorMessage}")]
    public static partial void DeleteTestDataFailed(
        this ILogger logger, LogLevel level, double elapsedMs, int errorCode, string errorName, string? errorMessage);

    [LoggerMessage(EventId = 1208, Level = LogLevel.Information, Message = "Inbox processor started its loop")]
    public static partial void ProcessorStarted(this ILogger logger);

    [LoggerMessage(EventId = 1209, Level = LogLevel.Information, Message = "Inbox processor stopped its loop")]
    public static partial void ProcessorStopped(this ILogger logger);

    [LoggerMessage(
        EventId = 1210, Level = LogLevel.Debug,
        Message = "Inbox ProcessPendingAsync claimed {Claimed} messages: {Succeeded} succeeded, {Retried} retried, and {Failed} failed in {ElapsedMs} ms")]
    public static partial void ProcessPendingSucceeded(
        this ILogger logger, int claimed, int succeeded, int retried, int failed, double elapsedMs);

    [LoggerMessage(
        EventId = 1211,
        Message = "Inbox ProcessPendingAsync failed after {ElapsedMs} ms: {ErrorCode} {ErrorName} {ErrorMessage}")]
    public static partial void ProcessPendingFailed(
        this ILogger logger, LogLevel level, double elapsedMs, int errorCode, string errorName, string? errorMessage);

    [LoggerMessage(
        EventId = 1212, Level = LogLevel.Debug,
        Message = "Inbox {Operation} gave the count {Count} in {ElapsedMs} ms")]
    public static partial void ManagementSucceeded(this ILogger logger, string operation, int count, double elapsedMs);

    [LoggerMessage(
        EventId = 1213,
        Message = "Inbox {Operation} failed after {ElapsedMs} ms: {ErrorCode} {ErrorName} {ErrorMessage}")]
    public static partial void ManagementFailed(
        this ILogger logger, LogLevel level, string operation, double elapsedMs, int errorCode, string errorName, string? errorMessage);

    [LoggerMessage(
        EventId = 1214, Level = LogLevel.Debug,
        Message = "Inbox {Operation} of tenant {Tenant} gave the count {Count} in {ElapsedMs} ms")]
    public static partial void TenantManagementSucceeded(
        this ILogger logger, string operation, string? tenant, int count, double elapsedMs);

    [LoggerMessage(
        EventId = 1215,
        Message = "Inbox {Operation} of tenant {Tenant} failed after {ElapsedMs} ms: {ErrorCode} {ErrorName} {ErrorMessage}")]
    public static partial void TenantManagementFailed(
        this ILogger logger, LogLevel level, string operation, string? tenant, double elapsedMs, int errorCode, string errorName,
        string? errorMessage);
}
