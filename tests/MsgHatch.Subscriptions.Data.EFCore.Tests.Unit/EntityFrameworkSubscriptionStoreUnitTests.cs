using MsgHatch.Subscriptions.Data.EFCore.Tests.Unit.TestTypes;
using MsgHatch.Subscriptions.Tests.Unit.Contracts;
using MsgHatch.Subscriptions.Tests.Unit.TestTypes;

namespace MsgHatch.Subscriptions.Data.EFCore.Tests.Unit;

/// <summary>The subscription store contract on the EF Core SQLite provider (D318).</summary>
public sealed class EntityFrameworkSubscriptionStoreUnitTests(EntityFrameworkAcmeAndGlobexScenario scenario)
    : SubscriptionStoreTestsBase<EntityFrameworkSubscriptionStore>(
        EntityFrameworkSubscriptionStoreTestData.StoresOnANewFile(),
        EntityFrameworkSubscriptionStoreTestData.Of,
        scenario),
      IClassFixture<EntityFrameworkAcmeAndGlobexScenario>
{
    public static TheoryData<string, string, int> BrokenRowRows => new()
    {
        { "unknown level", "UPDATE MsgHatchSubscription SET Level = 'Team'", 415 },
        { "owner that does not match its level", "UPDATE MsgHatchSubscription SET Level = 'User'", 415 },
        { "invalid settings JSON", "UPDATE MsgHatchSubscription SET Settings = '{'", 415 },
        { "newer settings version", "UPDATE MsgHatchSubscription SET SettingsVersion = 2", 426 },
        { "named users that are not a JSON array", "UPDATE MsgHatchSubscription SET NamedUsers = '{'", 415 },
    };

    private EntityFrameworkSubscriptionStoreTestData Data => EntityFrameworkSubscriptionStoreTestData.Of(Store);

    [Fact]
    public async Task AddAsync_SubscriptionForNamedUsers_WritesTheFieldsAsTextAndEmptyTextsForNoTenantAndNoUser()
    {
        // Act
        await Store.AddAsync(Scenario.S11, Ct);

        // Assert
        Assert.Equal(
            ["", "Company", "100", "", "D", "Channel", "Email", "NamedUsers", "[\"2\"]", "0", "2030-01-01 00:00:00"],
            Assert.Single(Data.Database.Rows(
                "SELECT TenantId, Level, CompanyId, UserId, MessageType, Kind, Channel, Recipients, NamedUsers, CAST(Mandatory AS TEXT), CreatedAtUtc FROM MsgHatchSubscription")));
    }

    [Theory]
    [MemberData(nameof(BrokenRowRows))]
    public async Task GetAsync_RowThatAPersonBroke_GivesTheErrorOfTheRow(string row, string change, int expectedCode)
    {
        // Arrange
        await Store.AddAsync(Scenario.S11, Ct);
        Data.Database.Execute(change);

        // Act
        var stored = await Store.GetAsync(Scenario.S11.Id, Ct);

        // Assert
        Assert.Equal((row, (int?)expectedCode), (row, stored.Error?.Type.Code));
    }

    [Fact]
    public async Task AddAsync_TableThatDoesNotExist_GivesUnavailable()
    {
        // Arrange
        Data.Database.Execute("DROP TABLE MsgHatchSubscription");

        // Act
        var error = await Store.AddAsync(Scenario.S6, Ct);

        // Assert
        Assert.Equal(503, error?.Type.Code);
    }
}
