using MsgHatch.Data.EFCore.PostgreSql.Tests.Integration.TestTypes;
using MsgHatch.Data.EFCore.Tests.Integration.TestTypes;
using MsgHatch.Domain.Persistence;
using MsgHatch.Domain.Tests.Unit.Contracts;
using MsgHatch.Domain.Tests.Unit.TestTypes;

namespace MsgHatch.Data.EFCore.PostgreSql.Tests.Integration;

/// <summary>The outbox contract on PostgreSQL with the dialect, on a database of the migrations of the package (D318, D320).</summary>
public sealed class EntityFrameworkOutboxStoreIntegTests(PostgreSqlFixture server)
    : OutboxStoreTestsBase<EntityFrameworkOutboxStore>(
        new EntityFrameworkOutboxStore(MigratedStores.ContextsOf(MigratedStores.Of(server, "outbox")), new MsgHatchJson(TestContentTypes.ARegistry())),
        static (store, key) => store.For(key),
        _ => new PostgreSqlOutboxStoreTestData(MigratedStores.Of(server, "outbox")))
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
            MigratedStores.ServicesOf(MigratedStores.Of(server, "outbox")).LogRecords(),
            static record => record.Message.Contains("FOR UPDATE SKIP LOCKED", StringComparison.Ordinal));
    }
}
