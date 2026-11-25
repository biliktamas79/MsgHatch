using MsgHatch.Data.EFCore.Tests.Integration.TestTypes;
using MsgHatch.Domain.Identity;
using MsgHatch.Subscriptions.Data.EFCore.PostgreSql.Tests.Integration.TestTypes;
using MsgHatch.Subscriptions.Tests.Unit.Contracts;
using MsgHatch.Subscriptions.Tests.Unit.TestTypes;

namespace MsgHatch.Subscriptions.Data.EFCore.PostgreSql.Tests.Integration;

/// <summary>The subscription store contract on PostgreSQL with the dialect, on a database of the migrations of the package (D318, D320), with a key at its maximum length (D327).</summary>
public sealed class EntityFrameworkSubscriptionStoreIntegTests(PostgreSqlFixture server, EntityFrameworkAcmeAndGlobexScenario scenario)
    : SubscriptionStoreTestsBase<EntityFrameworkSubscriptionStore>(
        key => MigratedSubscriptionStores.StoreOf(MigratedSubscriptionStores.Of(server, "subscriptions"), key),
        _ => new MigratedSubscriptionStores.TestData(MigratedSubscriptionStores.Of(server, "subscriptions")),
        scenario),
      IClassFixture<EntityFrameworkAcmeAndGlobexScenario>
{
    [Fact]
    public async Task AddAsync_KeyColumnsAtTheirMaximumLength_StoresTheSubscriptionAndASecondAddGivesConflict()
    {
        // Arrange
        var store = StoreOf(TenantKey.For(new TenantId(new string('€', 256))));
        var subscription = TestSubscriptions.A(
            owner: SubscriptionOwner.ForCompany(new CompanyId(new string('€', 256))), type: new string('M', 256), kind: SubscriptionKind.OptOut,
            channel: new ChannelId(new string('C', 256)));

        // Act
        var first = await store.AddAsync(subscription, Ct);
        var second = await store.AddAsync(subscription with { Id = Guid.NewGuid() }, Ct);

        // Assert
        Assert.Equal(((int?)null, (int?)409), (CodeOf(first), CodeOf(second)));
    }
}
