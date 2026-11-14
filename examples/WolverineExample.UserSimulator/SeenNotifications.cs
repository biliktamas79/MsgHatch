namespace WolverineExample.UserSimulator;

/// <summary>
/// The notifications that one simulated user already got, live or from a catch-up. The user logs each one once, and
/// sends the id of the last new one to the hub at the next catch-up. A web UI keeps the same id in its local storage.
/// The handlers of SignalR and the catch-up run on different threads, so each member takes a lock.
/// </summary>
public sealed class SeenNotifications
{
    // ponytail: the ids grow for the life of the process; keep only the ids of the last catch-up window if a user runs for days
    private readonly HashSet<Guid> _ids = [];
    private readonly Lock _lock = new();
    private Guid? _lastId;

    /// <summary>The id of the last new notification, or null when the user got none.</summary>
    public Guid? LastId
    {
        get
        {
            lock (_lock)
            {
                return _lastId;
            }
        }
    }

    /// <summary>Records a notification, and makes it the last one when it is new.</summary>
    /// <param name="id">The id of the notification.</param>
    /// <returns>True when the user did not have the notification yet.</returns>
    public bool TryAdd(Guid id)
    {
        lock (_lock)
        {
            if (!_ids.Add(id))
            {
                return false;
            }

            _lastId = id;
            return true;
        }
    }
}
