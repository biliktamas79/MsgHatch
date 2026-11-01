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
}
