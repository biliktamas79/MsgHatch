using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using MsgHatch.Domain.Identity;
using MsgHatch.SignalR;
using MsgHatch.Subscriptions;
using WolverineExample.Consumer.Notifications;
using WolverineExample.Contracts;

namespace WolverineExample.Consumer.Channels;

/// <summary>
/// The SignalR hub of the UI notifications. It adds each connection to the group of the company of its user, from the
/// directory of its tenant, because the SignalR transport sends a company message to that group. The transport sends
/// through its hub context. A client calls <see cref="GetMissedNotificationsAsync"/> after each connect, for the
/// notifications that the hub sent while the client had no connection.
/// </summary>
/// <param name="services">The services of the hub call.</param>
[Authorize]
public sealed class NotificationHub(IServiceProvider services) : Hub
{
    /// <inheritdoc/>
    public override async Task OnConnectedAsync()
    {
        if (HubCaller.Of(Context.User) is { } caller && await CompanyOfAsync(caller) is { } company)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, SignalRGroups.ForCompany(caller.Tenant, company), Context.ConnectionAborted);
        }

        await base.OnConnectedAsync();
    }

    /// <summary>
    /// Gives the notifications of the caller and of the company of the caller that the client possibly missed, oldest first
    /// (<see cref="NotificationHistory"/>). The tenant and the user come from the token, and the company from the directory.
    /// </summary>
    /// <param name="lastMessageId">The id of the last notification that the client got, or null.</param>
    /// <returns>The notifications.</returns>
    /// <exception cref="HubException">The token has no valid tenant or user, or the outbox is not available.</exception>
    [HubMethodName(HubMethods.GetMissedNotifications)]
    public async Task<IReadOnlyList<MissedNotification>> GetMissedNotificationsAsync(Guid? lastMessageId)
    {
        var caller = HubCaller.Of(Context.User) ?? throw new HubException("The token has no valid tenant or user.");
        var missed = await services.GetRequiredService<NotificationHistory>()
            .GetMissedAsync(caller.Tenant, caller.User, await CompanyOfAsync(caller), lastMessageId, Context.ConnectionAborted);
        return missed.IsError ? throw new HubException($"The missed notifications are not available: {missed.Error.Type.Name}") : missed.Value;
    }

    private async Task<CompanyId?> CompanyOfAsync(HubCaller caller)
    {
        var directory = services.GetRequiredKeyedService<IRecipientDirectory>(TenantKey.For(caller.Tenant));
        var users = await directory.GetUsersAsync([caller.User], Context.ConnectionAborted);
        return users is { IsError: false, Value: [var profile] } ? profile.Company : null;
    }
}
