using Microsoft.Extensions.Logging;

namespace MsgHatch.Data.EFCore.PostgreSql;

/// <summary>The log events of the PostgreSQL dispatch signal, with the event ids 3300 to 3399 (section 4.16).</summary>
internal static partial class Log
{
    [LoggerMessage(
        EventId = 3300, Level = LogLevel.Warning,
        Message = "The PostgreSQL dispatch signal does not listen on its database, so the loops wait for their due times and their poll until it listens again after {Pause}")]
    public static partial void NotListening(this ILogger logger, TimeSpan pause, Exception exception);

    [LoggerMessage(
        EventId = 3301, Level = LogLevel.Information,
        Message = "The PostgreSQL dispatch signal listens on its database again")]
    public static partial void ListensAgain(this ILogger logger);
}
