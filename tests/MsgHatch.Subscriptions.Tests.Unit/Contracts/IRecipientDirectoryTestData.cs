namespace MsgHatch.Subscriptions.Tests.Unit.Contracts;

/// <summary>
/// What <see cref="IRecipientDirectory"/> cannot do for its contract tests (section 5.5): reset the storage of the
/// directory, and seed companies and users into it.
/// </summary>
public interface IRecipientDirectoryTestData
{
    /// <summary>Removes all companies and users of the directory.</summary>
    ValueTask ResetAsync(CancellationToken ct);

    /// <summary>Writes the companies and the users into the directory.</summary>
    ValueTask SeedAsync(IReadOnlyList<CompanyProfile> companies, IReadOnlyList<UserProfile> users, CancellationToken ct);
}
