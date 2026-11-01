using Microsoft.Extensions.Logging;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Dispatch;

namespace MsgHatch.Domain.Outbox;

/// <summary>The log events of the outbox, with the event ids 1300 to 1399 (section 4.16).</summary>
internal static partial class Log
{
    [LoggerMessage(
        EventId = 1300, Level = LogLevel.Debug,
        Message = "Outbox AddAsync stored {Count} messages of tenant {Tenant} with flags {MessageFlags} in {ElapsedMs} ms")]
    public static partial void AddSucceeded(
        ILogger logger, int count, string? tenant, MessageFlags messageFlags, double elapsedMs);

    [LoggerMessage(
        EventId = 1301,
        Message = "Outbox AddAsync of {Count} messages of tenant {Tenant} with flags {MessageFlags} failed after {ElapsedMs} ms: {ErrorCode} {ErrorName} {ErrorMessage}")]
    public static partial void AddFailed(
        ILogger logger, LogLevel level, int count, string? tenant, MessageFlags messageFlags, double elapsedMs,
        int errorCode, string errorName, string? errorMessage);

    [LoggerMessage(
        EventId = 1302, Level = LogLevel.Debug,
        Message = "Outbox DeleteTestDataAsync deleted {Count} messages in {ElapsedMs} ms")]
    public static partial void DeleteTestDataSucceeded(ILogger logger, int count, double elapsedMs);

    [LoggerMessage(
        EventId = 1303,
        Message = "Outbox DeleteTestDataAsync failed after {ElapsedMs} ms: {ErrorCode} {ErrorName} {ErrorMessage}")]
    public static partial void DeleteTestDataFailed(
        ILogger logger, LogLevel level, double elapsedMs, int errorCode, string errorName, string? errorMessage);

    [LoggerMessage(
        EventId = 1304, Level = LogLevel.Debug,
        Message = "Outbox ClaimDueAsync claimed {Count} messages of {Channel} with the down reason {DownReason} in {ElapsedMs} ms")]
    public static partial void ClaimSucceeded(
        ILogger logger, int count, string channel, ChannelDownReason? downReason, double elapsedMs);

    [LoggerMessage(
        EventId = 1305,
        Message = "Outbox ClaimDueAsync of {Channel} failed after {ElapsedMs} ms: {ErrorCode} {ErrorName} {ErrorMessage}")]
    public static partial void ClaimFailed(
        ILogger logger, LogLevel level, string channel, double elapsedMs, int errorCode, string errorName, string? errorMessage);

    [LoggerMessage(
        EventId = 1306, Level = LogLevel.Debug,
        Message = "Outbox RecordTryAsync recorded try {TryNumber} with the outcome {Outcome} of outgoing message {OutgoingMessageId} on {Channel} of tenant {Tenant} with flags {MessageFlags} in {ElapsedMs} ms")]
    public static partial void RecordTrySucceeded(
        ILogger logger, int tryNumber, PublishOutcome outcome, Guid outgoingMessageId, string channel, string? tenant,
        MessageFlags messageFlags, double elapsedMs);

    [LoggerMessage(
        EventId = 1307,
        Message = "Outbox RecordTryAsync of try {TryNumber} with the outcome {Outcome} of outgoing message {OutgoingMessageId} on {Channel} of tenant {Tenant} with flags {MessageFlags} failed after {ElapsedMs} ms: {ErrorCode} {ErrorName} {ErrorMessage}")]
    public static partial void RecordTryFailed(
        ILogger logger, LogLevel level, int tryNumber, PublishOutcome outcome, Guid outgoingMessageId, string channel,
        string? tenant, MessageFlags messageFlags, double elapsedMs, int errorCode, string errorName, string? errorMessage);

    [LoggerMessage(
        EventId = 1308, Level = LogLevel.Debug,
        Message = "Outbox GetDownChannelsAsync gave {Count} down channels in {ElapsedMs} ms")]
    public static partial void GetDownChannelsSucceeded(ILogger logger, int count, double elapsedMs);

    [LoggerMessage(
        EventId = 1309,
        Message = "Outbox GetDownChannelsAsync failed after {ElapsedMs} ms: {ErrorCode} {ErrorName} {ErrorMessage}")]
    public static partial void GetDownChannelsFailed(
        ILogger logger, LogLevel level, double elapsedMs, int errorCode, string errorName, string? errorMessage);

    [LoggerMessage(
        EventId = 1310, Level = LogLevel.Debug,
        Message = "Outbox {Operation} of {Channel} with the reason {Reason} gave {Changed} in {ElapsedMs} ms")]
    public static partial void ChannelStateChangeSucceeded(
        ILogger logger, string operation, string channel, ChannelDownReason? reason, bool changed, double elapsedMs);

    [LoggerMessage(
        EventId = 1311,
        Message = "Outbox {Operation} of {Channel} with the reason {Reason} failed after {ElapsedMs} ms: {ErrorCode} {ErrorName} {ErrorMessage}")]
    public static partial void ChannelStateChangeFailed(
        ILogger logger, LogLevel level, string operation, string channel, ChannelDownReason? reason, double elapsedMs,
        int errorCode, string errorName, string? errorMessage);

    [LoggerMessage(EventId = 1312, Level = LogLevel.Information, Message = "Outbox dispatcher started its loops")]
    public static partial void DispatcherStarted(ILogger logger);

    [LoggerMessage(EventId = 1313, Level = LogLevel.Information, Message = "Outbox dispatcher stopped its loops")]
    public static partial void DispatcherStopped(ILogger logger);

    [LoggerMessage(
        EventId = 1314, Level = LogLevel.Debug,
        Message = "Outbox DispatchPendingAsync claimed {Claimed} messages: {Succeeded} succeeded, {Retried} retried, and {Failed} failed in {ElapsedMs} ms")]
    public static partial void DispatchPendingSucceeded(
        ILogger logger, int claimed, int succeeded, int retried, int failed, double elapsedMs);

    [LoggerMessage(
        EventId = 1315,
        Message = "Outbox DispatchPendingAsync failed after {ElapsedMs} ms: {ErrorCode} {ErrorName} {ErrorMessage}")]
    public static partial void DispatchPendingFailed(
        ILogger logger, LogLevel level, double elapsedMs, int errorCode, string errorName, string? errorMessage);
}
