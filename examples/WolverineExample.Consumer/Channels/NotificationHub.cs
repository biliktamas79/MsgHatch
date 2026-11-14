using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using MsgHatch.Domain.Identity;
using MsgHatch.SignalR;
using MsgHatch.Subscriptions;

namespace WolverineExample.Consumer.Channels;

/// <summary>
/// The SignalR hub of the UI notifications. It adds each connection to the group of the company of its user, from the
/// directory of its tenant, because the SignalR transport sends a company message to that group. The transport sends
/// through its hub context, so the hub needs no other method.
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

    private async Task<CompanyId?> CompanyOfAsync(HubCaller caller)
    {
        var directory = services.GetRequiredKeyedService<IRecipientDirectory>(TenantKey.For(caller.Tenant));
        var users = await directory.GetUsersAsync([caller.User], Context.ConnectionAborted);
        return users is { IsError: false, Value: [var profile] } ? profile.Company : null;
    }
}
