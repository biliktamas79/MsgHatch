using MsgHatch.Channels.Email;
using MsgHatch.Data.EFCore.Tests.Integration.TestTypes;
using MsgHatch.Domain.Identity;
using MsgHatch.Subscriptions.Data.EFCore.Tests.Integration.TestTypes;
using MsgHatch.Subscriptions.Tests.Unit.Contracts;
using MsgHatch.Subscriptions.Tests.Unit.TestTypes;

namespace MsgHatch.Subscriptions.Data.EFCore.Tests.Integration;

public sealed class EntityFrameworkSubscriptionStoreIntegTests(SqlServerFixture server, EntityFrameworkAcmeAndGlobexScenario scenario)
    : SubscriptionStoreTestsBase<EntityFrameworkSubscriptionStore>(
        EntityFrameworkSubscriptionStoreTestData.Of(server, "subscriptions").StoreOf,
        _ => EntityFrameworkSubscriptionStoreTestData.Of(server, "subscriptions"),
        scenario),
      IClassFixture<EntityFrameworkAcmeAndGlobexScenario>
{
    private const string _isoTime = "2030-01-01T00:00:00.0000000Z";

    private TestDatabase Database => EntityFrameworkSubscriptionStoreTestData.Of(server, "subscriptions").Raw;

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
            Assert.Single(await Database.RowsAsync(
                """
                SELECT TenantId, Level, CompanyId, UserId, MessageType, Kind, Channel, SettingsType, SettingsVersion, Settings,
                    Recipients, NamedUsers, Mandatory, CreatedAtUtc, LastModifiedAtUtc
                FROM [msghatch].[Subscription]
                """,
                Ct)));
    }

    [Fact]
    public async Task FindForDeliveryAsync_RowWithANewerSettingsVersion_GivesUnsupportedVersion()
    {
        // Arrange
        await Store.AddAsync(Scenario.S6, Ct);
        await Database.ExecuteAsync("UPDATE [msghatch].[Subscription] SET SettingsVersion = 2", Ct);

        // Act
        var found = await Store.FindForDeliveryAsync(Scenario.S6.Type, [], [new UserId("1")], Ct);

        // Assert
        Assert.Equal(426, found.Error?.Type.Code);
    }
}
