using System.Globalization;
using MsgHatch.Domain.Identity;

namespace MsgHatch.Subscriptions.AdminUI;

/// <summary>The addresses of the pages of the subscription admin UI, so that a host can link to a page (D239).</summary>
public static class SubscriptionAdminRoutes
{
    /// <summary>The address of the subscription list.</summary>
    public const string List = "/msghatch/subscriptions";

    /// <summary>The address of the subscription list with the dialog of a new subscription open (D260).</summary>
    public const string New = List + "?new=true";

    /// <summary>The address of the routing verification (D163).</summary>
    public const string Verification = List + "/verification";

    internal const string EditTemplate = List + "/{Id:guid}";

    /// <summary>Gives the address of the edit page of one subscription.</summary>
    /// <param name="id">The id of the subscription.</param>
    /// <returns>For example <c>/msghatch/subscriptions/3f2c…</c>.</returns>
    public static string Edit(Guid id) => $"{List}/{id.ToString("D", CultureInfo.InvariantCulture)}";

    /// <summary>Gives the address of the subscription list with the filters of a message type and a level (D276).</summary>
    /// <param name="type">The message type.</param>
    /// <param name="level">The level of the owner.</param>
    /// <returns>For example <c>/msghatch/subscriptions?type=OrderShipped&amp;level=Company</c>, with each value escaped.</returns>
    public static string ListOf(MessageType type, SubscriptionLevel level) =>
        $"{List}?type={Uri.EscapeDataString(type.Name)}&level={Uri.EscapeDataString(level.ToString())}";
}
