using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;

namespace MsgHatch.Subscriptions.Verification;

/// <summary>
/// The recipient directory of one variation of a routing verification: the test profile is the directory result for the
/// test user, and the directory gives the other profiles (section 4.18).
/// </summary>
internal sealed class TestProfileDirectory(IRecipientDirectory directory, UserProfile testProfile) : IRecipientDirectory
{
    public async ValueTask<ErrorOr<IReadOnlyList<UserProfile>>> GetUsersAsync(IReadOnlyList<UserId> users, CancellationToken ct) =>
        WithTheTestProfile(await directory.GetUsersAsync(users, ct));

    public async ValueTask<ErrorOr<IReadOnlyList<UserProfile>>> GetCompanyUsersAsync(CompanyId company, CancellationToken ct) =>
        WithTheTestProfile(await directory.GetCompanyUsersAsync(company, ct));

    public ValueTask<ErrorOr<CompanyProfile?>> GetCompanyAsync(CompanyId company, CancellationToken ct) => directory.GetCompanyAsync(company, ct);

    private ErrorOr<IReadOnlyList<UserProfile>> WithTheTestProfile(ErrorOr<IReadOnlyList<UserProfile>> users) =>
        users.IsError
            ? users.Error
            : ErrorOr<IReadOnlyList<UserProfile>>.ForValue([.. users.Value.Select(user => user.Id == testProfile.Id ? testProfile : user)]);
}
