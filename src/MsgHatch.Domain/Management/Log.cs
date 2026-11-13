using Microsoft.Extensions.Logging;

namespace MsgHatch.Domain.Management;

/// <summary>The log events of the delivery management, with the event ids 1800 to 1899 (section 4.16).</summary>
internal static partial class Log
{
    [LoggerMessage(
        EventId = 1800, Level = LogLevel.Debug,
        Message = "Delivery {Operation} of tenant {Tenant} gave the count {Count} in {ElapsedMs} ms")]
    public static partial void ReadSucceeded(this ILogger logger, string operation, string? tenant, int count, double elapsedMs);

    [LoggerMessage(
        EventId = 1801,
        Message = "Delivery {Operation} of tenant {Tenant} failed after {ElapsedMs} ms: {ErrorCode} {ErrorName} {ErrorMessage}")]
    public static partial void OperationFailed(
        this ILogger logger, LogLevel level, string operation, string? tenant, double elapsedMs, int errorCode, string errorName,
        string? errorMessage);

    [LoggerMessage(
        EventId = 1802, Level = LogLevel.Information,
        Message = "Delivery {Operation} of channel {Channel} completed in {ElapsedMs} ms")]
    public static partial void ChannelChanged(this ILogger logger, string operation, string channel, double elapsedMs);

    [LoggerMessage(
        EventId = 1803, Level = LogLevel.Information,
        Message = "Delivery {Operation} of tenant {Tenant} with the filters {Filters} matched {Matched} and changed {Changed} messages in {ElapsedMs} ms")]
    public static partial void MessagesChanged(
        this ILogger logger, string operation, string? tenant, string filters, int matched, int changed, double elapsedMs);

    [LoggerMessage(
        EventId = 1804, Level = LogLevel.Information,
        Message = "Delivery {Operation} deleted {InboxMessages} received and {OutboxMessages} outgoing messages in {ElapsedMs} ms")]
    public static partial void MessagesDeleted(this ILogger logger, string operation, int inboxMessages, int outboxMessages, double elapsedMs);
}
