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
}
