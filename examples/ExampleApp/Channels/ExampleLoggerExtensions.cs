using MsgHatch.Subscriptions;

namespace ExampleApp.Channels;

/// <summary>
/// The log events of the example, 9000 to 9099, as source-generated extension methods of <see cref="ILogger"/>. A
/// generated method checks the level first, and does not box its values.
/// </summary>
public static partial class ExampleLoggerExtensions
{
    /// <summary>The event id of <see cref="EmailSent"/>.</summary>
    public const int EmailSentId = 9000;

    /// <summary>The event id of <see cref="BrokerMessageReceived"/>.</summary>
    public const int BrokerMessageReceivedId = 9001;

    /// <summary>One email of the log email client.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="messageId">The id of the outgoing message.</param>
    /// <param name="to">The address.</param>
    /// <param name="subject">The subject.</param>
    /// <param name="body">The text body, or else the HTML body.</param>
    [LoggerMessage(EmailSentId, LogLevel.Information, "Email {MessageId} to {To}: {Subject} | {Body}")]
    public static partial void EmailSent(this ILogger logger, Guid messageId, string to, string subject, string? body);

    /// <summary>One broker message of the local Wolverine queue.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="type">The message type of the notice.</param>
    /// <param name="text">The text of the notice.</param>
    [LoggerMessage(BrokerMessageReceivedId, LogLevel.Information, "Broker message {Type}: {Text}")]
    public static partial void BrokerMessageReceived(this ILogger logger, string type, string text);

    /// <summary>The event id of <see cref="RoutingVerified"/>.</summary>
    public const int RoutingVerifiedId = 9002;

    /// <summary>The event id of <see cref="RoutingCheckFailed"/>.</summary>
    public const int RoutingCheckFailedId = 9003;

    /// <summary>The event id of <see cref="RoutingCheckSkipped"/>.</summary>
    public const int RoutingCheckSkippedId = 9004;

    /// <summary>The counts of the routing verification at startup (D162).</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="passed">The number of passed rows.</param>
    /// <param name="failed">The number of failed rows.</param>
    /// <param name="skipped">The number of skipped rows.</param>
    [LoggerMessage(RoutingVerifiedId, LogLevel.Information, "Routing verification: {Passed} passed, {Failed} failed, {Skipped} skipped")]
    public static partial void RoutingVerified(this ILogger logger, int passed, int failed, int skipped);

    /// <summary>One failed row of the routing verification (D162).</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="messageType">The message type.</param>
    /// <param name="testCase">The test case.</param>
    /// <param name="level">The owner level, or null.</param>
    /// <param name="channel">The channel, or null.</param>
    /// <param name="language">The language.</param>
    /// <param name="errorCode">The error code.</param>
    /// <param name="errorName">The error name.</param>
    [LoggerMessage(
        RoutingCheckFailedId, LogLevel.Warning,
        "Routing check failed: type {MessageType}, test case {TestCase}, level {Level}, channel {Channel}, language {Language}: {ErrorCode} {ErrorName}")]
    public static partial void RoutingCheckFailed(
        this ILogger logger, string messageType, string? testCase, SubscriptionLevel? level, string? channel, string? language, int? errorCode,
        string? errorName);

    /// <summary>One skipped row of the routing verification: a message type with no test case, or a subscription that no test user reached (D162).</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="messageType">The message type.</param>
    /// <param name="testCase">The test case, or null.</param>
    /// <param name="level">The owner level, or null.</param>
    /// <param name="channel">The channel, or null.</param>
    /// <param name="language">The language, or null.</param>
    [LoggerMessage(RoutingCheckSkippedId, LogLevel.Information, "Routing check skipped: type {MessageType}, test case {TestCase}, level {Level}, channel {Channel}, language {Language}")]
    public static partial void RoutingCheckSkipped(
        this ILogger logger, string messageType, string? testCase, SubscriptionLevel? level, string? channel, string? language);
}
