using MsgHatch.Domain.Identity;

namespace MsgHatch.Subscriptions;

/// <summary>
/// The owner of a subscription: the default level, one company, or one user (D72). An owner has no tenant, because the
/// subscription store of a tenant holds only the subscriptions of that tenant (D116).
/// </summary>
public readonly record struct SubscriptionOwner
{
    private SubscriptionOwner(SubscriptionLevel level, CompanyId? company, UserId? user)
    {
        Level = level;
        Company = company;
        User = user;
    }

    /// <summary>The level of the owner.</summary>
    public SubscriptionLevel Level { get; }

    /// <summary>The company of a company owner, else null.</summary>
    public CompanyId? Company { get; }

    /// <summary>The user of a user owner, else null.</summary>
    public UserId? User { get; }

    /// <summary>The default level of the tenant of the store.</summary>
    public static SubscriptionOwner Default { get; }

    /// <summary>Gives the owner of the company level.</summary>
    /// <param name="company">The company.</param>
    /// <returns>The owner.</returns>
    public static SubscriptionOwner ForCompany(CompanyId company) => new(SubscriptionLevel.Company, company, user: null);

    /// <summary>Gives the owner of the user level.</summary>
    /// <param name="user">The user.</param>
    /// <returns>The owner.</returns>
    public static SubscriptionOwner ForUser(UserId user) => new(SubscriptionLevel.User, company: null, user);
}
