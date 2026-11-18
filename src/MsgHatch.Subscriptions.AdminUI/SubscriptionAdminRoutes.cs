using System.Globalization;

namespace MsgHatch.Subscriptions.AdminUI;

/// <summary>The addresses of the pages of the subscription admin UI, so that a host can link to a page (D239).</summary>
public static class SubscriptionAdminRoutes
{
    /// <summary>The address of the subscription list.</summary>
    public const string List = "/msghatch/subscriptions";

    /// <summary>The address of the applied subscriptions of each message type, with the title "Applied subscriptions" (D273).</summary>
    public const string MessageTypes = List + "/message-types";

    /// <summary>The address of the subscription list with the dialog of a new subscription open (D260).</summary>
    public const string New = List + "?new=true";

    /// <summary>The address of the routing verification (D163).</summary>
    public const string Verification = List + "/verification";

    internal const string EditTemplate = List + "/{Id:guid}";

    /// <summary>Gives the address of the edit page of one subscription.</summary>
    /// <param name="id">The id of the subscription.</param>
    /// <returns>For example <c>/msghatch/subscriptions/3f2c…</c>.</returns>
    public static string Edit(Guid id) => $"{List}/{id.ToString("D", CultureInfo.InvariantCulture)}";
}
