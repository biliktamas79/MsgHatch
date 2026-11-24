using MsgHatch.Data.EFCore.PostgreSql.Tests.Integration.TestTypes;
using MsgHatch.Data.EFCore.Tests.Integration.TestTypes;
using MsgHatch.Domain.Management;
using MsgHatch.Domain.Persistence;
using MsgHatch.Domain.Tests.Unit.Contracts;
using MsgHatch.Domain.Tests.Unit.TestTypes;

namespace MsgHatch.Data.EFCore.PostgreSql.Tests.Integration;

/// <summary>The inbox contract on PostgreSQL with the dialect, on a database of the migrations of the package (D318, D320).</summary>
public sealed class EntityFrameworkInboxStoreIntegTests(PostgreSqlFixture server)
    : InboxStoreTestsBase<EntityFrameworkInboxStore>(
        new EntityFrameworkInboxStore(MigratedStores.ContextsOf(MigratedStores.Of(server, "inbox")), new MsgHatchJson(TestContentTypes.ARegistry())),
        static (store, key) => store.For(key),
        _ => new PostgreSqlInboxStoreTestData(MigratedStores.Of(server, "inbox")))
{
    [Fact]
    public async Task ClaimDueAsync_DueMessage_ClaimsItWithTheSkipLockedStatementOfTheDialect()
    {
        // Arrange
        var message = await AddAsync(AMessage(T1));

        // Act
        var result = await ClaimAsync();

        // Assert
        Assert.Equal(message.Delivery.Id, Assert.Single(result.Value.Messages).Delivery.Id);
        Assert.Contains(
            MigratedStores.ServicesOf(MigratedStores.Of(server, "inbox")).LogRecords(),
            static record => record.Message.Contains("FOR UPDATE SKIP LOCKED", StringComparison.Ordinal));
    }

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
