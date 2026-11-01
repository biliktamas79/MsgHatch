using Microsoft.Extensions.Logging;
using MsgHatch.Domain.Delivery;

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
}
