using Microsoft.Extensions.Logging;
using MsgHatch.Domain.Delivery;

namespace MsgHatch.Domain.Dispatch;

/// <summary>The log events of the dispatch loops, with the event ids 1400 to 1499 (section 4.16).</summary>
internal static partial class Log
{
    [LoggerMessage(
        EventId = 1400, Level = LogLevel.Error,
        Message = "Dispatch of outgoing message {OutgoingMessageId} on {Channel} of tenant {Tenant} with flags {MessageFlags} failed with an exception: {ErrorCode} {ErrorName}")]
    public static partial void OutgoingMessageFailed(
        ILogger logger, Guid outgoingMessageId, string channel, string? tenant, MessageFlags messageFlags, int errorCode,
        string errorName, Exception exception);

    [LoggerMessage(
        EventId = 1401, Level = LogLevel.Error,
        Message = "Processing of received message {MessageId} of {MessageType} of tenant {Tenant} with flags {MessageFlags} failed with an exception: {ErrorCode} {ErrorName}")]
    public static partial void ReceivedMessageFailed(
        ILogger logger, Guid messageId, string messageType, string? tenant, MessageFlags messageFlags, int errorCode,
        string errorName, Exception exception);

    [LoggerMessage(
        EventId = 1402, Level = LogLevel.Error,
        Message = "Channel {Channel} is down for all its publishers after transient errors in a row: {ErrorCode} {ErrorName}")]
    public static partial void ChannelDown(ILogger logger, string channel, int? errorCode, string? errorName);

    [LoggerMessage(
        EventId = 1403, Level = LogLevel.Information,
        Message = "Channel {Channel} is up again after a health check, after {DownSeconds} seconds down")]
    public static partial void ChannelUp(ILogger logger, string channel, double downSeconds);
}
