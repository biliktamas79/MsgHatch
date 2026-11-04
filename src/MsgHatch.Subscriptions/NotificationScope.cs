using MsgHatch.Domain.Identity;

namespace MsgHatch.Subscriptions;

/// <summary>
/// The scope of a domain event for the subscription router: one or more users, or one company (D16, D74). The scope
/// function of <c>WithScope</c> gives it. A duplicate user id stays in the scope, and the resolution removes it.
/// </summary>
public readonly struct NotificationScope
{
    private NotificationScope(ReadOnlyMemory<UserId> users, CompanyId? company)
    {
        Users = users;
        Company = company;
    }

    /// <summary>The users of a user scope, in their order. Empty for a company scope.</summary>
    public ReadOnlyMemory<UserId> Users { get; }

    /// <summary>The company of a company scope, else null.</summary>
    public CompanyId? Company { get; }

    /// <summary>Gives a user scope.</summary>
    /// <param name="users">The users.</param>
    /// <returns>The scope.</returns>
    public static NotificationScope ForUsers(ReadOnlyMemory<UserId> users) => new(users, company: null);

    /// <summary>Gives a user scope from string ids, for example the ids of a domain event.</summary>
    /// <param name="users">The user ids. Each one is a valid <see cref="UserId"/>.</param>
    /// <returns>The scope.</returns>
    public static NotificationScope ForUsers(IReadOnlyList<string> users)
    {
        ArgumentNullException.ThrowIfNull(users);
        var ids = new UserId[users.Count];
        for (var index = 0; index < ids.Length; index++)
        {
            ids[index] = new UserId(users[index]);
        }

        return new(ids, company: null);
    }

    /// <summary>Gives a company scope.</summary>
    /// <param name="company">The company.</param>
    /// <returns>The scope.</returns>
    public static NotificationScope ForCompany(CompanyId company) => new(ReadOnlyMemory<UserId>.Empty, company);
}
