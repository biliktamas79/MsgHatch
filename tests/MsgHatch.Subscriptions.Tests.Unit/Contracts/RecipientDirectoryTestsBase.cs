using MsgHatch.Domain.Identity;
using MsgHatch.Email;
using MsgHatch.Subscriptions.Tests.Unit.TestTypes;

namespace MsgHatch.Subscriptions.Tests.Unit.Contracts;

/// <summary>
/// The use cases of <see cref="IRecipientDirectory"/> (section 5.5), with the directory examples U1 to U4 of
/// <c>docs/subscription-domain.md</c>, on the companies and users of the shared scenario. Each implementation gets them
/// with one subclass that only builds the directory and its test data abstraction. Each test seeds the scenario into
/// empty storage, and resets the storage after it.
/// </summary>
/// <typeparam name="TDirectory">The directory type.</typeparam>
public abstract partial class RecipientDirectoryTestsBase<TDirectory> : IAsyncLifetime
    where TDirectory : class, IRecipientDirectory
{
    protected RecipientDirectoryTestsBase(TDirectory directory, IRecipientDirectoryTestData testData)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(testData);
        Directory = directory;
        TestData = testData;
    }

    protected TDirectory Directory { get; }

    protected IRecipientDirectoryTestData TestData { get; }

    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await TestData.ResetAsync(Ct);
        await TestData.SeedAsync(Scenario.Companies, Scenario.Users, Ct);
    }

    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return TestData.ResetAsync(Ct);
    }

    /// <summary>Gives a user profile as <c>{id} {company} {UI language} {email address}</c>.</summary>
    protected static string Describe(UserProfile user) =>
        $"{user.Id.Value} {user.Company?.Value ?? "-"} {user.UiLanguage} {EmailOf(user.Addresses)}";

    /// <summary>Gives a company profile as <c>{id} {default language} {email address}</c>, or <c>null</c>.</summary>
    protected static string Describe(CompanyProfile? company) =>
        company is null ? "null" : $"{company.Id.Value} {company.DefaultLanguage} {EmailOf(company.Addresses)}";

    private static string EmailOf(IReadOnlyDictionary<ChannelId, string> addresses) =>
        addresses.TryGetValue(EmailChannel.Id, out var email) ? email : "-";
}
