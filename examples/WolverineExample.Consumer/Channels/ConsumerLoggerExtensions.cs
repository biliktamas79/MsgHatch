namespace WolverineExample.Consumer.Channels;

/// <summary>The log events of the consumer, 9200 to 9299, as source-generated extension methods of <see cref="ILogger"/>.</summary>
public static partial class ConsumerLoggerExtensions
{
    /// <summary>The event id of <see cref="EmailSent"/>.</summary>
    public const int EmailSentId = 9200;

    /// <summary>The event id of <see cref="EventAccepted"/>.</summary>
    public const int EventAcceptedId = 9201;

    /// <summary>One email of the log email client.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="messageId">The id of the outgoing message.</param>
    /// <param name="to">The address.</param>
    /// <param name="subject">The subject.</param>
    /// <param name="body">The text body, or else the HTML body.</param>
    [LoggerMessage(EmailSentId, LogLevel.Information, "Email {MessageId} to {To}: {Subject} | {Body}")]
    public static partial void EmailSent(this ILogger logger, Guid messageId, string to, string subject, string? body);

    /// <summary>The inbox stored one event of the broker.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="eventType">The type name of the event.</param>
    /// <param name="messageId">The message id of the event.</param>
    /// <param name="tenant">The tenant of the event.</param>
    [LoggerMessage(EventAcceptedId, LogLevel.Information, "Accepted {EventType} {MessageId} of the tenant {Tenant}")]
    public static partial void EventAccepted(this ILogger logger, string eventType, Guid messageId, string? tenant);
}
