using MsgHatch.Domain.Identity;

namespace MsgHatch.Subscriptions;

/// <summary>
/// The recipients of a subscription (D89). For <see cref="RecipientKind.ScopeUsers"/> and
/// <see cref="RecipientKind.Company"/>, <see cref="Users"/> is empty. Two values are equal when their kinds are equal and
/// their users are equal in the same order, so that a subscription read back from a store is equal to the stored one.
/// </summary>
/// <param name="Kind">The kind of the recipients.</param>
/// <param name="Users">The named users, 1 to 100 different user ids, for <see cref="RecipientKind.NamedUsers"/>.</param>
public sealed record SubscriptionRecipients(RecipientKind Kind, IReadOnlyList<UserId> Users)
{
    /// <summary>The scope users of the owner.</summary>
    public static SubscriptionRecipients ScopeUsers { get; } = new(RecipientKind.ScopeUsers, []);

    /// <summary>The company that the event concerns.</summary>
    public static SubscriptionRecipients Company { get; } = new(RecipientKind.Company, []);

    /// <summary>Gives named users, in their order.</summary>
    /// <param name="users">The users. The value keeps a copy of the list.</param>
    /// <returns>The recipients.</returns>
    public static SubscriptionRecipients ForUsers(IReadOnlyList<UserId> users)
    {
        ArgumentNullException.ThrowIfNull(users);
        return new(RecipientKind.NamedUsers, [.. users]);
    }

    /// <inheritdoc />
    public bool Equals(SubscriptionRecipients? other) =>
        other is not null && Kind == other.Kind && Users.SequenceEqual(other.Users);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Kind);
        foreach (var user in Users)
        {
            hash.Add(user);
        }

        return hash.ToHashCode();
    }
}
