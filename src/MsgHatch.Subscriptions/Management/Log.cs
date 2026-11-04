using Microsoft.Extensions.Logging;

namespace MsgHatch.Subscriptions.Management;

/// <summary>
/// The log events of the subscription management API, with event ids of the subscriptions area 1100 to 1199
/// (section 4.16). No event holds a user id, a company id, or channel settings (D37).
/// </summary>
internal static partial class Log
{
    [LoggerMessage(
        EventId = 1120, Level = LogLevel.Debug,
        Message = "Subscription manager listed {ChannelCount} channels of tenant {Tenant} in {ElapsedMs} ms")]
    public static partial void GetChannelsSucceeded(ILogger logger, int channelCount, string? tenant, double elapsedMs);

    [LoggerMessage(
        EventId = 1121,
        Message = "Subscription manager list of the channels of tenant {Tenant} failed after {ElapsedMs} ms: {ErrorCode} {ErrorName} {ErrorMessage}")]
    public static partial void GetChannelsFailed(
        ILogger logger, LogLevel level, string? tenant, double elapsedMs, int errorCode, string errorName, string? errorMessage);

    [LoggerMessage(
        EventId = 1122, Level = LogLevel.Debug,
        Message = "Subscription manager listed {MessageTypeCount} message types of tenant {Tenant} in {ElapsedMs} ms")]
    public static partial void GetMessageTypesSucceeded(ILogger logger, int messageTypeCount, string? tenant, double elapsedMs);

    [LoggerMessage(
        EventId = 1123,
        Message = "Subscription manager list of the message types of tenant {Tenant} failed after {ElapsedMs} ms: {ErrorCode} {ErrorName} {ErrorMessage}")]
    public static partial void GetMessageTypesFailed(
        ILogger logger, LogLevel level, string? tenant, double elapsedMs, int errorCode, string errorName, string? errorMessage);

    [LoggerMessage(
        EventId = 1124, Level = LogLevel.Debug,
        Message = "Subscription manager listed {SubscriptionCount} subscriptions of tenant {Tenant} in {ElapsedMs} ms")]
    public static partial void GetSubscriptionsSucceeded(ILogger logger, int subscriptionCount, string? tenant, double elapsedMs);

    [LoggerMessage(
        EventId = 1125,
        Message = "Subscription manager list of the subscriptions of tenant {Tenant} failed after {ElapsedMs} ms: {ErrorCode} {ErrorName} {ErrorMessage}")]
    public static partial void GetSubscriptionsFailed(
        ILogger logger, LogLevel level, string? tenant, double elapsedMs, int errorCode, string errorName, string? errorMessage);

    [LoggerMessage(
        EventId = 1126, Level = LogLevel.Debug,
        Message = "Subscription manager read subscription {SubscriptionId} of tenant {Tenant} in {ElapsedMs} ms")]
    public static partial void GetSubscriptionSucceeded(ILogger logger, Guid subscriptionId, string? tenant, double elapsedMs);

    [LoggerMessage(
        EventId = 1127,
        Message = "Subscription manager read of subscription {SubscriptionId} of tenant {Tenant} failed after {ElapsedMs} ms: {ErrorCode} {ErrorName} {ErrorMessage}")]
    public static partial void GetSubscriptionFailed(
        ILogger logger, LogLevel level, Guid subscriptionId, string? tenant, double elapsedMs, int errorCode, string errorName,
        string? errorMessage);

    [LoggerMessage(
        EventId = 1128, Level = LogLevel.Debug,
        Message = "Subscription manager created subscription {SubscriptionId} of {MessageType} at level {Level} of tenant {Tenant} in {ElapsedMs} ms")]
    public static partial void CreateSucceeded(
        ILogger logger, Guid subscriptionId, string messageType, SubscriptionLevel level, string? tenant, double elapsedMs);

    [LoggerMessage(
        EventId = 1129,
        Message = "Subscription manager create of a subscription of {MessageType} at level {Level} of tenant {Tenant} failed after {ElapsedMs} ms: {ErrorCode} {ErrorName} {ErrorMessage}")]
    public static partial void CreateFailed(
        ILogger logger, LogLevel logLevel, string messageType, SubscriptionLevel level, string? tenant, double elapsedMs, int errorCode,
        string errorName, string? errorMessage);

    [LoggerMessage(
        EventId = 1130, Level = LogLevel.Debug,
        Message = "Subscription manager updated subscription {SubscriptionId} to revision {Revision} of tenant {Tenant} in {ElapsedMs} ms")]
    public static partial void UpdateSucceeded(ILogger logger, Guid subscriptionId, int revision, string? tenant, double elapsedMs);

    [LoggerMessage(
        EventId = 1131,
        Message = "Subscription manager update of subscription {SubscriptionId} from revision {ExpectedRevision} of tenant {Tenant} failed after {ElapsedMs} ms: {ErrorCode} {ErrorName} {ErrorMessage}")]
    public static partial void UpdateFailed(
        ILogger logger, LogLevel level, Guid subscriptionId, int expectedRevision, string? tenant, double elapsedMs, int errorCode,
        string errorName, string? errorMessage);

    [LoggerMessage(
        EventId = 1132, Level = LogLevel.Debug,
        Message = "Subscription manager deleted subscription {SubscriptionId} of tenant {Tenant} in {ElapsedMs} ms")]
    public static partial void DeleteSucceeded(ILogger logger, Guid subscriptionId, string? tenant, double elapsedMs);

    [LoggerMessage(
        EventId = 1133,
        Message = "Subscription manager delete of subscription {SubscriptionId} of tenant {Tenant} failed after {ElapsedMs} ms: {ErrorCode} {ErrorName} {ErrorMessage}")]
    public static partial void DeleteFailed(
        ILogger logger, LogLevel level, Guid subscriptionId, string? tenant, double elapsedMs, int errorCode, string errorName,
        string? errorMessage);
}
