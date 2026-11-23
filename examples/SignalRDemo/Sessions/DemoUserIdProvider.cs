using Microsoft.AspNetCore.SignalR;
using MsgHatch.SignalR;

namespace SignalRDemo.Sessions;

/// <summary>
/// Gives each connection with a token the user name of <see cref="SignalRUsers"/>, <c>{TenantId}:{UserId}</c>, because
/// the SignalR transport sends a user message to that name (D114). So the user <c>anna</c> of <c>eu</c> never gets a
/// message of the user <c>anna</c> of <c>us</c>.
/// </summary>
public sealed class DemoUserIdProvider : IUserIdProvider
{
    /// <inheritdoc/>
    public string? GetUserId(HubConnectionContext connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        return DemoAccount.Of(connection.User) is { } account ? SignalRUsers.ForUser(account.Tenant, account.User) : null;
    }
}
