using ExampleApp.Notices;

namespace ExampleApp.Channels;

/// <summary>
/// The Wolverine handler of the local broker queue of the example. It writes each broker message to the log, in place of
/// a receiver behind a broker server.
/// </summary>
/// <param name="logger">The logger.</param>
public sealed class BrokerMessageLogger(ILogger<BrokerMessageLogger> logger)
{
    /// <summary>Writes one broker message to the log.</summary>
    /// <param name="notice">The payload of the broker message.</param>
    public void Handle(Notice notice)
    {
        ArgumentNullException.ThrowIfNull(notice);
        logger.BrokerMessageReceived(notice.Type, notice.Text);
    }
}
