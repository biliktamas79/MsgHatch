using MsgHatch.Data.EFCore.SqlServer.Tests.Integration.TestTypes;
using MsgHatch.Data.EFCore.Tests.Integration.TestTypes;
using MsgHatch.Domain.Persistence;
using MsgHatch.Domain.Tests.Unit.Contracts;
using MsgHatch.Domain.Tests.Unit.TestTypes;

namespace MsgHatch.Data.EFCore.SqlServer.Tests.Integration;

/// <summary>The outbox contract on SQL Server with the dialect, on a database of the migrations of the package (D318, D320).</summary>
public sealed class EntityFrameworkOutboxStoreIntegTests(SqlServerFixture server)
    : OutboxStoreTestsBase<EntityFrameworkOutboxStore>(
        new EntityFrameworkOutboxStore(MigratedStores.ContextsOf(MigratedStores.Of(server, "outbox")), new MsgHatchJson(TestContentTypes.ARegistry())),
        static (store, key) => store.For(key),
        _ => new EntityFrameworkOutboxStoreTestData(MigratedStores.Of(server, "outbox")))
{
    [Fact]
    public async Task ClaimDueAsync_DueMessage_ClaimsItWithTheReadPastStatementOfTheDialect()
    {
        // Arrange
        var message = await AddAsync(AMessage(T1));

        // Act
        var result = await ClaimAsync();

        // Assert
        Assert.Equal(message.Delivery.Id, Assert.Single(result.Value.Messages).Delivery.Id);
        Assert.Contains(
            MigratedStores.ServicesOf(MigratedStores.Of(server, "outbox")).LogRecords(),
            static record => record.Message.Contains("WITH (ROWLOCK, READPAST, UPDLOCK)", StringComparison.Ordinal));
    }
}
