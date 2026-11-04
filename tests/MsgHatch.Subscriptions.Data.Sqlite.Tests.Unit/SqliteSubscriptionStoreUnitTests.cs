using MsgHatch.Data.Sqlite;
using MsgHatch.Data.Sqlite.Tests.Unit.TestTypes;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Persistence;
using MsgHatch.Email;
using MsgHatch.Subscriptions.Data.Sqlite.Tests.Unit.TestTypes;
using MsgHatch.Subscriptions.Tests.Unit.Contracts;
using MsgHatch.Subscriptions.Tests.Unit.TestTypes;

namespace MsgHatch.Subscriptions.Data.Sqlite.Tests.Unit;

public sealed class SqliteSubscriptionStoreUnitTests(SqliteAcmeAndGlobexScenario scenario)
    : SubscriptionStoreTestsBase<SqliteSubscriptionStore>(
        SqliteSubscriptionStoreTestData.StoresOn(TestDatabases.NewOptions().ConnectionString),
        static store => new SqliteSubscriptionStoreTestData(store.ConnectionString),
        scenario),
      IClassFixture<SqliteAcmeAndGlobexScenario>
{
    private const string _isoTime = "2030-01-01T00:00:00.0000000Z";

    public static TheoryData<string, string, int> BrokenRowRows => new()
    {
        { "unknown level", "UPDATE MsgHatchSubscription SET Level = 'Team'", 415 },
        { "owner that does not match its level", "UPDATE MsgHatchSubscription SET Level = 'User'", 415 },
        { "NULL revision", "UPDATE MsgHatchSubscription SET Revision = NULL", 415 },
        { "invalid time", "UPDATE MsgHatchSubscription SET CreatedAtUtc = 'yesterday'", 415 },
        { "NULL message type", "UPDATE MsgHatchSubscription SET MessageType = NULL", 415 },
        { "invalid settings JSON", "UPDATE MsgHatchSubscription SET Settings = '{'", 415 },
        { "newer settings version", "UPDATE MsgHatchSubscription SET SettingsVersion = 2", 426 },
        { "named users that are not a JSON array", "UPDATE MsgHatchSubscription SET NamedUsers = '{'", 415 },
        { "named users that are NULL", "UPDATE MsgHatchSubscription SET NamedUsers = NULL", 415 },
    };

    [Fact]
    public async Task AddAsync_SecondDefaultSubscriptionForTheSameTypeChannelAndRecipientKind_GivesConflict()
    {
        // Arrange
        await Store.AddAsync(Scenario.S1, Ct);

        // Act
        var error = await Store.AddAsync(Scenario.S1 with { Id = Scenario.LabelId(50), Settings = new EmailChannelSettings("hu") }, Ct);

        // Assert
        Assert.Equal(409, CodeOf(error));
    }

    [Fact]
    public async Task AddAsync_SecondOptOutForAllChannelsOfTheSameOwnerAndType_GivesConflict()
    {
        // Arrange
        await Store.AddAsync(Scenario.S4, Ct);

        // Act
        var error = await Store.AddAsync(Scenario.S4 with { Id = Scenario.LabelId(51) }, Ct);

        // Assert
        Assert.Equal(409, CodeOf(error));
    }

    [Fact]
    public async Task AddAsync_SubscriptionForNamedUsers_WritesTheFieldsAsTextAndEmptyTextsForNoTenantAndNoUser()
    {
        // Arrange
        var settings = ScenarioContentTypes.SettingsJson.Serialize(EmailChannel.Id, Scenario.S11.Settings!).Value.Json;

        // Act
        await Store.AddAsync(Scenario.S11, Ct);

        // Assert
        Assert.Equal(
            ["", "Company", "100", "", "D", "Channel", "Email", "Settings:Email", "1", settings, "NamedUsers", "[\"2\"]", "0", _isoTime, _isoTime],
            Assert.Single(TestDatabases.Rows(
                Store.ConnectionString,
                """
                SELECT TenantId, Level, CompanyId, UserId, MessageType, Kind, Channel, SettingsType, SettingsVersion, Settings,
                    Recipients, NamedUsers, Mandatory, CreatedAtUtc, LastModifiedAtUtc
                FROM MsgHatchSubscription
                """)));
    }

    [Fact]
    public async Task AddAsync_DatabaseThatSqliteOutboxStoreShares_EachStoreKeepsItsOwnSchemaVersion()
    {
        // Arrange
        var outbox = new SqliteOutboxStore(Store.ConnectionString, new MsgHatchJson(ScenarioContentTypes.Registry));
        await outbox.ClaimDueAsync(EmailChannel.Id, 1, TestHost.Start.UtcDateTime, TimeSpan.FromSeconds(30), Ct);

        // Act
        var error = await Store.AddAsync(Scenario.S6, Ct);

        // Assert
        Assert.Equal(
            ((Error?)null, "1", "1", Scenario.S6),
            (error,
                Assert.Single(Assert.Single(TestDatabases.Rows(Store.ConnectionString, "SELECT Version FROM MsgHatchSchema"))),
                Assert.Single(Assert.Single(TestDatabases.Rows(Store.ConnectionString, "SELECT Version FROM MsgHatchSubscriptionSchema"))),
                (await Store.GetAsync(Scenario.S6.Id, Ct)).Value));
    }

    [Theory]
    [MemberData(nameof(BrokenRowRows))]
    public async Task GetAsync_RowThatAPersonBroke_GivesTheErrorOfTheRow(string row, string change, int expectedCode)
    {
        // Arrange
        await Store.AddAsync(Scenario.S11, Ct);
        TestDatabases.Execute(Store.ConnectionString, change);

        // Act
        var stored = await Store.GetAsync(Scenario.S11.Id, Ct);

        // Assert
        Assert.Equal((row, (int?)expectedCode), (row, stored.Error?.Type.Code));
    }

    [Fact]
    public async Task FindForDeliveryAsync_RowWithANewerSettingsVersion_GivesUnsupportedVersion()
    {
        // Arrange
        await Store.AddAsync(Scenario.S6, Ct);
        TestDatabases.Execute(Store.ConnectionString, "UPDATE MsgHatchSubscription SET SettingsVersion = 2");

        // Act
        var found = await Store.FindForDeliveryAsync(Scenario.S6.Type, [], [new UserId("1")], Ct);

        // Assert
        Assert.Equal(426, found.Error?.Type.Code);
    }

    [Fact]
    public async Task AddAsync_DatabaseInAFolderThatDoesNotExist_GivesUnavailable()
    {
        // Arrange
        var missing = Path.Combine(Path.GetTempPath(), "msghatch-tests", Guid.NewGuid().ToString("N"), "subscriptions.db");
        var store = new SqliteSubscriptionStore($"Data Source={missing}", TenantKey.None, ScenarioContentTypes.SettingsJson);

        // Act
        var error = await store.AddAsync(Scenario.S6, Ct);

        // Assert
        Assert.Equal(503, CodeOf(error));
    }
}
