using Microsoft.AspNetCore.SignalR;
using MsgHatch.Channels.SignalR;

namespace WolverineExample.Consumer.Channels;

/// <summary>
/// Gives each SignalR connection the user name of <see cref="SignalRUsers"/>, <c>{tenant}:{user}</c>, because the SignalR
/// transport sends a user message to that name (D114).
/// </summary>
public sealed class TenantUserIdProvider : IUserIdProvider
{
    /// <inheritdoc/>
    public string? GetUserId(HubConnectionContext connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        return HubCaller.Of(connection.User) is { } caller ? SignalRUsers.ForUser(caller.Tenant, caller.User) : null;
    }
}
