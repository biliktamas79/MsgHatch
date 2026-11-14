namespace WolverineExample.Contracts;

/// <summary>The names of the methods that a client calls on the SignalR hub of the consumer.</summary>
public static class HubMethods
{
    /// <summary>
    /// Gives the notifications that the caller possibly missed, oldest first, as a list of <see cref="MissedNotification"/>.
    /// Its argument is the id of the last notification that the client got, or null when it has none.
    /// </summary>
    public const string GetMissedNotifications = "GetMissedNotifications";
}
