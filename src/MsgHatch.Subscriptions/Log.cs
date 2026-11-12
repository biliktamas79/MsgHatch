using Microsoft.Extensions.Logging;
using MsgHatch.Domain.Delivery;

namespace MsgHatch.Subscriptions;

/// <summary>
/// The log events of the subscription store, the recipient directory, and the subscription router, with event ids of the
/// subscriptions area 1100 to 1199 (section 4.16). No event holds a user id, a company id, an address, or a domain event (D37).
/// </summary>
internal static partial class Log
{
    [LoggerMessage(
        EventId = 1100, Level = LogLevel.Debug,
        Message = "Subscription store found {SubscriptionCount} subscriptions of {MessageType} for {CompanyCount} companies and {UserCount} users of tenant {Tenant} in {ElapsedMs} ms")]
    public static partial void FindForDeliverySucceeded(
        this ILogger logger, int subscriptionCount, string messageType, int companyCount, int userCount, string? tenant, double elapsedMs);

    [LoggerMessage(
        EventId = 1101,
        Message = "Subscription store find of {MessageType} for {CompanyCount} companies and {UserCount} users of tenant {Tenant} failed after {ElapsedMs} ms: {ErrorCode} {ErrorName} {ErrorMessage}")]
    public static partial void FindForDeliveryFailed(
        this ILogger logger, LogLevel level, string messageType, int companyCount, int userCount, string? tenant, double elapsedMs,
        int errorCode, string errorName, string? errorMessage);

    [LoggerMessage(
        EventId = 1102, Level = LogLevel.Debug,
        Message = "Subscription store query gave {SubscriptionCount} subscriptions of tenant {Tenant} in {ElapsedMs} ms")]
    public static partial void QuerySucceeded(this ILogger logger, int subscriptionCount, string? tenant, double elapsedMs);

    [LoggerMessage(
        EventId = 1103,
        Message = "Subscription store query of tenant {Tenant} failed after {ElapsedMs} ms: {ErrorCode} {ErrorName} {ErrorMessage}")]
    public static partial void QueryFailed(
        this ILogger logger, LogLevel level, string? tenant, double elapsedMs, int errorCode, string errorName, string? errorMessage);

    [LoggerMessage(
        EventId = 1104, Level = LogLevel.Debug,
        Message = "Subscription store read subscription {SubscriptionId} of tenant {Tenant}, found {Found}, in {ElapsedMs} ms")]
    public static partial void GetSucceeded(this ILogger logger, Guid subscriptionId, string? tenant, bool found, double elapsedMs);

    [LoggerMessage(
        EventId = 1105,
        Message = "Subscription store read of subscription {SubscriptionId} of tenant {Tenant} failed after {ElapsedMs} ms: {ErrorCode} {ErrorName} {ErrorMessage}")]
    public static partial void GetFailed(
        this ILogger logger, LogLevel level, Guid subscriptionId, string? tenant, double elapsedMs, int errorCode, string errorName,
        string? errorMessage);

    [LoggerMessage(
        EventId = 1106, Level = LogLevel.Debug,
        Message = "Subscription store added subscription {SubscriptionId} of {MessageType} at level {Level} of tenant {Tenant} in {ElapsedMs} ms")]
    public static partial void AddSucceeded(
        this ILogger logger, Guid subscriptionId, string messageType, SubscriptionLevel level, string? tenant, double elapsedMs);

    [LoggerMessage(
        EventId = 1107,
        Message = "Subscription store add of subscription {SubscriptionId} of {MessageType} at level {Level} of tenant {Tenant} failed after {ElapsedMs} ms: {ErrorCode} {ErrorName} {ErrorMessage}")]
    public static partial void AddFailed(
        this ILogger logger, LogLevel logLevel, Guid subscriptionId, string messageType, SubscriptionLevel level, string? tenant,
        double elapsedMs, int errorCode, string errorName, string? errorMessage);

    [LoggerMessage(
        EventId = 1108, Level = LogLevel.Debug,
        Message = "Subscription store updated subscription {SubscriptionId} to revision {Revision} of tenant {Tenant} in {ElapsedMs} ms")]
    public static partial void UpdateSucceeded(this ILogger logger, Guid subscriptionId, int revision, string? tenant, double elapsedMs);

    [LoggerMessage(
        EventId = 1109,
        Message = "Subscription store update of subscription {SubscriptionId} from revision {ExpectedRevision} of tenant {Tenant} failed after {ElapsedMs} ms: {ErrorCode} {ErrorName} {ErrorMessage}")]
    public static partial void UpdateFailed(
        this ILogger logger, LogLevel level, Guid subscriptionId, int expectedRevision, string? tenant, double elapsedMs, int errorCode,
        string errorName, string? errorMessage);

    [LoggerMessage(
        EventId = 1110, Level = LogLevel.Debug,
        Message = "Subscription store deleted subscription {SubscriptionId} of tenant {Tenant} in {ElapsedMs} ms")]
    public static partial void DeleteSucceeded(this ILogger logger, Guid subscriptionId, string? tenant, double elapsedMs);

    [LoggerMessage(
        EventId = 1111,
        Message = "Subscription store delete of subscription {SubscriptionId} of tenant {Tenant} failed after {ElapsedMs} ms: {ErrorCode} {ErrorName} {ErrorMessage}")]
    public static partial void DeleteFailed(
        this ILogger logger, LogLevel level, Guid subscriptionId, string? tenant, double elapsedMs, int errorCode, string errorName,
        string? errorMessage);

    [LoggerMessage(
        EventId = 1112, Level = LogLevel.Debug,
        Message = "Recipient directory gave {ProfileCount} profiles of {UserCount} users of tenant {Tenant} in {ElapsedMs} ms")]
    public static partial void GetUsersSucceeded(this ILogger logger, int profileCount, int userCount, string? tenant, double elapsedMs);

    [LoggerMessage(
        EventId = 1113,
        Message = "Recipient directory read of {UserCount} users of tenant {Tenant} failed after {ElapsedMs} ms: {ErrorCode} {ErrorName} {ErrorMessage}")]
    public static partial void GetUsersFailed(
        this ILogger logger, LogLevel level, int userCount, string? tenant, double elapsedMs, int errorCode, string errorName,
        string? errorMessage);

    [LoggerMessage(
        EventId = 1114, Level = LogLevel.Debug,
        Message = "Recipient directory gave {ProfileCount} profiles of the users of a company of tenant {Tenant} in {ElapsedMs} ms")]
    public static partial void GetCompanyUsersSucceeded(this ILogger logger, int profileCount, string? tenant, double elapsedMs);

    [LoggerMessage(
        EventId = 1115,
        Message = "Recipient directory read of the users of a company of tenant {Tenant} failed after {ElapsedMs} ms: {ErrorCode} {ErrorName} {ErrorMessage}")]
    public static partial void GetCompanyUsersFailed(
        this ILogger logger, LogLevel level, string? tenant, double elapsedMs, int errorCode, string errorName, string? errorMessage);

    [LoggerMessage(
        EventId = 1116, Level = LogLevel.Debug,
        Message = "Recipient directory read a company of tenant {Tenant}, found {Found}, in {ElapsedMs} ms")]
    public static partial void GetCompanySucceeded(this ILogger logger, string? tenant, bool found, double elapsedMs);

    [LoggerMessage(
        EventId = 1117,
        Message = "Recipient directory read of a company of tenant {Tenant} failed after {ElapsedMs} ms: {ErrorCode} {ErrorName} {ErrorMessage}")]
    public static partial void GetCompanyFailed(
        this ILogger logger, LogLevel level, string? tenant, double elapsedMs, int errorCode, string errorName, string? errorMessage);

    [LoggerMessage(
        EventId = 1118, Level = LogLevel.Debug,
        Message = "Subscription router applied opt-out {SubscriptionId} at level {Level} on channel {Channel} for message {MessageId} of {MessageType} of tenant {Tenant} with flags {MessageFlags}")]
    public static partial void OptOutApplied(
        this ILogger logger, Guid messageId, string messageType, string channel, Guid subscriptionId, SubscriptionLevel level, string? tenant,
        MessageFlags messageFlags);
}
