using System.Globalization;
using ExampleApp.Recipients;
using Microsoft.Extensions.Options;
using MsgHatch.Data.Sqlite.Tests.Unit.TestTypes;
using MsgHatch.Domain.Identity;
using MsgHatch.Email;
using MsgHatch.Subscriptions;
using MsgHatch.Subscriptions.Tests.Unit.Contracts;

namespace ExampleApp.Tests.Unit.Recipients;

/// <summary>
/// The test data of <see cref="ExampleRecipientDirectory"/>: its own database file, which a reset deletes, and the seed
/// of profiles as rows with <c>int</c> ids (D73).
/// </summary>
public sealed class ExampleRecipientDirectoryTestData : IRecipientDirectoryTestData
{
    private readonly ExampleRecipientDirectory _directory;
    private readonly string _connectionString;

    private ExampleRecipientDirectoryTestData(ExampleRecipientDirectory directory, string connectionString)
    {
        _directory = directory;
        _connectionString = connectionString;
    }

    /// <summary>Makes a directory of no tenant on a new database file, and its test data.</summary>
    public static ExampleRecipientDirectory NewDirectory(out ExampleRecipientDirectoryTestData testData)
    {
        var options = TestDatabases.NewOptions();
        var directory = new ExampleRecipientDirectory(TenantKey.None, Options.Create(options));
        testData = new ExampleRecipientDirectoryTestData(directory, options.ConnectionString);
        return directory;
    }

    public ValueTask ResetAsync(CancellationToken ct)
    {
        TestDatabases.Delete(_connectionString);
        return ValueTask.CompletedTask;
    }

    public async ValueTask SeedAsync(IReadOnlyList<CompanyProfile> companies, IReadOnlyList<UserProfile> users, CancellationToken ct)
    {
        foreach (var company in companies)
        {
            Assert.Null(await _directory.SaveCompanyAsync(new ExampleCompany(IdOf(company.Id.Value), company.DefaultLanguage!, EmailOf(company.Addresses)), ct));
        }

        foreach (var user in users)
        {
            var companyId = user.Company is { } id ? IdOf(id.Value) : (int?)null;
            Assert.Null(await _directory.SaveUserAsync(new ExampleUser(IdOf(user.Id.Value), companyId, user.UiLanguage!, EmailOf(user.Addresses)), ct));
        }
    }

    private static int IdOf(string id) => int.Parse(id, CultureInfo.InvariantCulture);

    private static string? EmailOf(IReadOnlyDictionary<ChannelId, string> addresses) => addresses.GetValueOrDefault(EmailChannel.Id);
}
