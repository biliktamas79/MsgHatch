using Microsoft.Extensions.Logging;

namespace WolverineExample.Publisher;

/// <summary>The log events of the publisher, 9100 to 9199, as source-generated extension methods of <see cref="ILogger"/>.</summary>
public static partial class PublisherLoggerExtensions
{
    /// <summary>The event id of <see cref="EventSent"/>.</summary>
    public const int EventSentId = 9100;

    /// <summary>The broker accepted one domain event.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="eventType">The type name of the domain event.</param>
    /// <param name="queue">The queue of the broker.</param>
    [LoggerMessage(EventSentId, LogLevel.Information, "Sent {EventType} to the queue {Queue}")]
    public static partial void EventSent(this ILogger logger, string eventType, string queue);
}
