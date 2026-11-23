using MsgHatch.Domain.Routing;

namespace SignalRDemo.Notices;

/// <summary>
/// Gives the panes that must get a notification, from the state of each pane that the dashboard sends (D316): each
/// connected pane of the tenant of the notification, and of its company for a company target, or of its user for a
/// user target. The dashboard marks in red an expected pane that gets no notification, and a pane that gets a
/// notification that it does not expect.
/// </summary>
public static class ExpectedPanes
{
    /// <summary>Gives the ids of the panes that must get a notification.</summary>
    /// <param name="tenant">The tenant of the notification.</param>
    /// <param name="target">The target of the notification.</param>
    /// <param name="panes">The state of each pane.</param>
    /// <returns>The ids of the expected panes, in the order of the panes.</returns>
    public static IReadOnlyList<string> Of(string tenant, DeliveryTarget target, IEnumerable<PaneState> panes)
    {
        ArgumentNullException.ThrowIfNull(panes);
        return [.. panes.Where(pane => pane.Connected && pane.Tenant == tenant && IsTargetOf(pane, target)).Select(static pane => pane.Id)];
    }

    private static bool IsTargetOf(PaneState pane, DeliveryTarget target) => target.Kind switch
    {
        DeliveryTargetKind.Tenant => true,
        DeliveryTargetKind.Company => pane.Company == target.Id,
        DeliveryTargetKind.User => pane.User == target.Id,
        _ => false,
    };
}

/// <summary>The state of one pane of the dashboard, from the last join of its connection.</summary>
/// <param name="Id">The id of the pane, for example <c>P1</c>.</param>
/// <param name="Connected">True when the connection of the pane is connected.</param>
/// <param name="Tenant">The tenant of the connection, or null.</param>
/// <param name="Company">The company of the connection, or null.</param>
/// <param name="User">The user of the connection, or null for an anonymous connection.</param>
public sealed record PaneState(string Id, bool Connected, string? Tenant, string? Company, string? User);
