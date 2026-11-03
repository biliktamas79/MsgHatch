using MsgHatch.Domain.Identity;

namespace MsgHatch.SignalR;

/// <summary>
/// The SignalR user names of the users (D114). The host's <c>IUserIdProvider</c> must give the name of this class for
/// each connection of a user, because the transport sends a user message to that name.
/// </summary>
public static class SignalRUsers
{
    /// <summary>
    /// Gives the SignalR user name of a user: the user id with no tenant, and <c>{TenantId}:{UserId}</c> with a tenant.
    /// In each id, <c>%</c> becomes <c>%25</c> and <c>:</c> becomes <c>%3A</c>, so that two users never get the same name.
    /// </summary>
    /// <param name="tenant">The tenant of the user, or null for no tenant.</param>
    /// <param name="user">The user.</param>
    /// <returns>The user name.</returns>
    public static string ForUser(TenantId? tenant, UserId user) =>
        tenant is { } id
            ? $"{SignalRNames.Escape(id.Value)}:{SignalRNames.Escape(user.Value)}"
            : SignalRNames.Escape(user.Value);
}
