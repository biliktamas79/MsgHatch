using MsgHatch.Data.EFCore.MySql.Tests.Integration.TestTypes;
using MsgHatch.Data.EFCore.Tests.Integration.TestTypes;
using MsgHatch.Domain.Management;
using MsgHatch.Domain.Persistence;
using MsgHatch.Domain.Tests.Unit.Contracts;
using MsgHatch.Domain.Tests.Unit.TestTypes;

namespace MsgHatch.Data.EFCore.MySql.Tests.Integration;

/// <summary>
/// The inbox contract on MySQL with the dialect, on a database of the migrations of the package (D318, D320, D325). The
/// dialect gives no claim statement, so the claim of the base package runs.
/// </summary>
public sealed class EntityFrameworkInboxStoreIntegTests(MySqlFixture server)
    : InboxStoreTestsBase<EntityFrameworkInboxStore>(
        new EntityFrameworkInboxStore(MigratedStores.ContextsOf(MigratedStores.Of(server, "inbox")), new MsgHatchJson(TestContentTypes.ARegistry())),
        static (store, key) => store.For(key),
        _ => new MySqlInboxStoreTestData(MigratedStores.Of(server, "inbox")))
{
    [Fact]
    public async Task QueryAsync_DueTimeWithAPartOfAMicrosecond_GivesTheDueTimeCutDownToTheMicrosecond()
    {
        // Arrange
        await AddAsync(AMessage(T1, nextTryAtUtc: Now.AddTicks(19)));

        // Act
        var page = await TenantStore(T1).QueryAsync(new InboxQuery(), Ct);

        // Assert
        Assert.Equal(Now.AddTicks(10), Assert.Single(page.Value.Items).Delivery.NextTryAtUtc);
    }
}
