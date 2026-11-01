using Microsoft.Extensions.Logging;
using MsgHatch.Domain.Delivery;

namespace MsgHatch.Domain.Pipeline;

/// <summary>The log events of the pipeline, the routers, and the notification service, with the event ids 1000 to 1099 (section 4.16).</summary>
internal static partial class Log
{
    [LoggerMessage(
        EventId = 1000, Level = LogLevel.Debug,
        Message = "Router of {MessageType} gave {RouteCount} routes for message {MessageId} of tenant {Tenant} with flags {MessageFlags} in {ElapsedMs} ms")]
    public static partial void RouteSucceeded(
        ILogger logger, string messageType, int routeCount, Guid messageId, string? tenant, MessageFlags messageFlags,
        double elapsedMs);

    [LoggerMessage(
        EventId = 1001,
        Message = "Router of {MessageType} failed for message {MessageId} of tenant {Tenant} with flags {MessageFlags} after {ElapsedMs} ms: {ErrorCode} {ErrorName} {ErrorMessage}")]
    public static partial void RouteFailed(
        ILogger logger, LogLevel level, string messageType, Guid messageId, string? tenant, MessageFlags messageFlags,
        double elapsedMs, int errorCode, string errorName, string? errorMessage);

    [LoggerMessage(
        EventId = 1002, Level = LogLevel.Debug,
        Message = "Payload loader of {MessageType} gave {PayloadCount} payloads for {RouteCount} routes of message {MessageId} of tenant {Tenant} with flags {MessageFlags} in {ElapsedMs} ms")]
    public static partial void LoadSucceeded(
        ILogger logger, string messageType, int payloadCount, int routeCount, Guid messageId, string? tenant,
        MessageFlags messageFlags, double elapsedMs);

    [LoggerMessage(
        EventId = 1003,
        Message = "Payload loader of {MessageType} failed for {RouteCount} routes of message {MessageId} of tenant {Tenant} with flags {MessageFlags} after {ElapsedMs} ms: {ErrorCode} {ErrorName} {ErrorMessage}")]
    public static partial void LoadFailed(
        ILogger logger, LogLevel level, string messageType, int routeCount, Guid messageId, string? tenant,
        MessageFlags messageFlags, double elapsedMs, int errorCode, string errorName, string? errorMessage);

    [LoggerMessage(
        EventId = 1004, Level = LogLevel.Debug,
        Message = "Processed message {MessageId} of the event type {EventType} of tenant {Tenant} with flags {MessageFlags}: {Stored} stored and {Failed} failed in {ElapsedMs} ms")]
    public static partial void ProcessSucceeded(
        ILogger logger, Guid messageId, string eventType, string? tenant, MessageFlags messageFlags, int stored, int failed,
        double elapsedMs);

    [LoggerMessage(
        EventId = 1005,
        Message = "Processing of message {MessageId} of the event type {EventType} of tenant {Tenant} with flags {MessageFlags} failed after {ElapsedMs} ms: {ErrorCode} {ErrorName} {ErrorMessage}")]
    public static partial void ProcessFailed(
        ILogger logger, LogLevel level, Guid messageId, string eventType, string? tenant, MessageFlags messageFlags,
        double elapsedMs, int errorCode, string errorName, string? errorMessage);
}
