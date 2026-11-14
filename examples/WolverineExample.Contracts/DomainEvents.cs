namespace WolverineExample.Contracts;

/// <summary>
/// The names of the five domain events. The consumer uses each name as its MsgHatch message type, and as the SignalR
/// method of the notification of that message type.
/// </summary>
public static class DomainEvents
{
    /// <summary>The names, in the order of the events in the README of the consumer.</summary>
    public static IReadOnlyList<string> Names { get; } =
        [nameof(UserLogin), nameof(CompanyArchived), nameof(ProjectCreated), nameof(ProjectFinished), nameof(TenantMaintenance)];
}
