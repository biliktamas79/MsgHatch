using System.Security.Cryptography;
using System.Text;
using MsgHatch.Domain.Identity;
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
        { "unknown level", "UPDATE Subscription SET Level = 'Team'", 415 },
        { "owner that does not match its level", "UPDATE Subscription SET Level = 'User'", 415 },
        { "invalid settings JSON", "UPDATE Subscription SET Settings = '{'", 415 },
        { "newer settings version", "UPDATE Subscription SET SettingsVersion = 2", 426 },
        { "named users that are not a JSON array", "UPDATE Subscription SET NamedUsers = '{'", 415 },
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
                "SELECT TenantId, Level, CompanyId, UserId, MessageType, Kind, Channel, Recipients, NamedUsers, CAST(Mandatory AS TEXT), CreatedAtUtc FROM Subscription")));
    }

    [Fact]
    public async Task AddAsync_Subscription_KeepsTheSha256OfTheByteCountAndTheUtf8BytesOfEachKeyValueAsTheOwnerKey()
    {
        // Arrange
        var subscription = TestSubscriptions.A(
            owner: SubscriptionOwner.ForCompany(new CompanyId("Ő1")), type: "D", kind: SubscriptionKind.OptOut, channel: new ChannelId("Email"));
        static byte[] Value(string text)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            return [(byte)bytes.Length, (byte)(bytes.Length >> 8), (byte)(bytes.Length >> 16), (byte)(bytes.Length >> 24), .. bytes];
        }

        byte[] key = [.. Value(""), .. Value("D"), .. Value("Company"), .. Value("Ő1"), .. Value(""), .. Value("Email"), .. Value("ScopeUsers")];

        // Act
        await Store.AddAsync(subscription, Ct);

        // Assert
        Assert.Equal([[Convert.ToHexString(SHA256.HashData(key))]], Data.Database.Rows("SELECT hex(OwnerKey) FROM Subscription"));
    }

    [Fact]
    public async Task AddAsync_KeysThatDifferOnlyAtTheBorderOfTwoValues_StoresTwoSubscriptions()
    {
        // Arrange
        var first = TestSubscriptions.A(owner: SubscriptionOwner.ForUser(new UserId("ab")), kind: SubscriptionKind.OptOut, channel: new ChannelId("c"));
        var second = TestSubscriptions.A(owner: SubscriptionOwner.ForUser(new UserId("a")), kind: SubscriptionKind.OptOut, channel: new ChannelId("bc"));

        // Act
        var codes = (CodeOf(await Store.AddAsync(first, Ct)), CodeOf(await Store.AddAsync(second, Ct)));

        // Assert
        Assert.Equal(((int?)null, (int?)null), codes);
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
        Data.Database.Execute("DROP TABLE Subscription");

        // Act
        var error = await Store.AddAsync(Scenario.S6, Ct);

        // Assert
        Assert.Equal(503, error?.Type.Code);
    }
}
