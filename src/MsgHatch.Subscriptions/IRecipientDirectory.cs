using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;

namespace MsgHatch.Subscriptions;

/// <summary>
/// The directory of the users and the companies of one tenant, which the consumer implements (D76). It is a tenant
/// service, so a user id or a company id of another tenant is unknown (D114). Each method returns an error for a failure
/// and does not throw. A caller must not await one <see cref="ValueTask"/> two times.
/// </summary>
public interface IRecipientDirectory
{
    /// <summary>Gives the profiles of the known users. An unknown user is left out, with no error.</summary>
    /// <param name="users">The users.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The profiles, or an error.</returns>
    ValueTask<ErrorOr<IReadOnlyList<UserProfile>>> GetUsersAsync(IReadOnlyList<UserId> users, CancellationToken ct);

    /// <summary>Gives the profiles of the users of a company. An unknown company gives an empty list.</summary>
    /// <param name="company">The company.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The profiles, or an error.</returns>
    ValueTask<ErrorOr<IReadOnlyList<UserProfile>>> GetCompanyUsersAsync(CompanyId company, CancellationToken ct);

    /// <summary>Gives the profile of a company, or null for an unknown company.</summary>
    /// <param name="company">The company.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The profile, null, or an error.</returns>
    ValueTask<ErrorOr<CompanyProfile?>> GetCompanyAsync(CompanyId company, CancellationToken ct);
}
