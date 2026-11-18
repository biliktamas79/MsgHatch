using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;

namespace MsgHatch.Subscriptions.Management;

/// <summary>
/// The rules of a subscription draft that need no state (D234, rules 2.3 of <c>docs/subscription-domain.md</c>). Each
/// implementation of <see cref="ISubscriptionCommands"/> runs them before each check that needs the registry, the
/// directory, or the store, so each implementation gives the same first error for an input.
/// </summary>
public static class SubscriptionDraftRules
{
    private const int _maxNamedUsers = 100;

    /// <summary>Checks a draft with the rules that need no state.</summary>
    /// <param name="draft">The draft.</param>
    /// <returns>Null, or <see cref="Errors.ValidationFailed"/> with the message of the first rule that the draft breaks.</returns>
    /// <exception cref="ArgumentNullException">The draft is null.</exception>
    public static Error? Check(SubscriptionDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var recipients = draft.Recipients ?? SubscriptionRecipients.ScopeUsers;
        return !OwnerMatchesItsLevel(draft.Owner) ? Invalid("The owner does not match its level.")
            : draft.Kind == SubscriptionKind.OptOut ? OptOutError(draft, recipients)
            : draft.Channel is null || draft.Settings is null ? Invalid("A channel subscription has a channel and settings.")
            : RecipientsError(draft, recipients);
    }

    private static bool OwnerMatchesItsLevel(SubscriptionOwner owner) => owner.Level switch
    {
        SubscriptionLevel.Default => owner is { Company: null, User: null },
        SubscriptionLevel.Company => owner is { Company.Value: not null, User: null },
        _ => owner is { User.Value: not null, Company: null },
    };

    private static Error? OptOutError(SubscriptionDraft draft, SubscriptionRecipients recipients) =>
        draft.Settings is not null ? Invalid("An opt-out has no settings.")
        : recipients.Kind != RecipientKind.ScopeUsers || draft.Mandatory ? Invalid("An opt-out is for the scope users, and is not mandatory.")
        : null;

    private static Error? RecipientsError(SubscriptionDraft draft, SubscriptionRecipients recipients) =>
        draft.Owner.Level == SubscriptionLevel.User && recipients.Kind != RecipientKind.ScopeUsers
            ? Invalid("A user subscription is for the scope users.")
        : draft.Mandatory && (draft.Owner.Level == SubscriptionLevel.User || recipients.Kind != RecipientKind.ScopeUsers)
            ? Invalid("A mandatory subscription is a default or company subscription for the scope users.")
        : recipients.Kind == RecipientKind.NamedUsers && !HasOneToHundredDifferentUsers(recipients.Users)
            ? Invalid("Named users are 1 to 100 different users.")
        : null;

    private static bool HasOneToHundredDifferentUsers(IReadOnlyList<UserId> users) =>
        users.Count is > 0 and <= _maxNamedUsers && users.Distinct().Count() == users.Count;

    private static Error Invalid(string message) => new(Errors.ValidationFailed, message);
}
