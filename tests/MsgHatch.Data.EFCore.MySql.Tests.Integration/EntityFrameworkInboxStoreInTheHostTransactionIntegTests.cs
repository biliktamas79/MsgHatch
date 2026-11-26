using Microsoft.EntityFrameworkCore;
using MsgHatch.Data.EFCore.MySql.Tests.Integration.TestTypes;
using MsgHatch.Data.EFCore.Tests.Integration.TestTypes;
using MsgHatch.Domain.Persistence;
using MsgHatch.Domain.Tests.Unit.Contracts;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using MsgHatch.Tests.Shared;
using MySqlConnector;

namespace MsgHatch.Data.EFCore.MySql.Tests.Integration;

/// <summary>
/// The inbox contract on MySQL with the inbox in the transaction of the host (D331, D332), on a database of the
/// migrations of the package: each add stages the row in a new context of the host, and the host saves it. The other tests
/// use a transaction of the host and the unique key of the server.
/// </summary>
public sealed class EntityFrameworkInboxStoreInTheHostTransactionIntegTests(MySqlFixture server)
    : InboxStoreTestsBase<EntityFrameworkInboxStore>(
        new EntityFrameworkInboxStore(MigratedStores.ContextsOf(MigratedStores.OfHost(server)), new MsgHatchJson(TestContentTypes.ARegistry())),
        (store, key) => new HostSavingInboxStore(store, key, () => MigratedStores.HostContextOf(MigratedStores.OfHost(server))),
        _ => new MySqlInboxStoreTestData(MigratedStores.OfHost(server)))
{
    private readonly MySqlTestDatabase _database = MigratedStores.OfHost(server);

    [Fact]
    public async Task SaveChangesAsync_CommitOfATransactionOfTheHost_StoresTheRowOfTheHostAndTheInboxRow()
    {
        // Arrange
        await using var host = MigratedStores.HostContextOf(_database);
        var order = AnOrder();
        await using var transaction = await host.Database.BeginTransactionAsync(Ct);
        host.Orders.Add(order);
        await Store.For(T1, host).AddAsync(AMessage(T1), Ct);
        await host.SaveChangesAsync(Ct);

        // Act
        await transaction.CommitAsync(Ct);

        // Assert
        Assert.Equal([["1", "1"]], await CountsAsync(order));
    }

    [Fact]
    public async Task SaveChangesAsync_RollbackOfATransactionOfTheHost_StoresNeitherRow()
    {
        // Arrange
        await using var host = MigratedStores.HostContextOf(_database);
        var order = AnOrder();
        await using var transaction = await host.Database.BeginTransactionAsync(Ct);
        host.Orders.Add(order);
        await Store.For(T1, host).AddAsync(AMessage(T1), Ct);
        await host.SaveChangesAsync(Ct);

        // Act
        await transaction.RollbackAsync(Ct);

        // Assert
        Assert.Equal([["0", "0"]], await CountsAsync(order));
    }

    [Fact]
    public async Task SaveChangesAsync_InboxIdThatIsStored_FailsOnTheUniqueKeyOfTheServerAndStoresNoRowOfTheHost()
    {
        // Arrange
        var stored = await AddAsync(AMessage(T1));
        await using var host = MigratedStores.HostContextOf(_database);
        var order = AnOrder();
        host.Orders.Add(order);
        await Store.For(T1, host).AddAsync(AMessage(T1, id: stored.Delivery.Id), Ct);

        // Act
        var exception = await Record.ExceptionAsync(() => host.SaveChangesAsync(Ct));

        // Assert
        Assert.Equal(MySqlErrorCode.DuplicateKeyEntry, Assert.IsType<MySqlException>(Assert.IsType<DbUpdateException>(exception).InnerException).ErrorCode);
        Assert.Equal([["0", "1"]], await CountsAsync(order));
    }

    private static TestOrder AnOrder() => new() { Text = $"order {Guid.NewGuid():N}", CreatedAtUtc = TestHost.Start.UtcDateTime };

    private Task<List<string?[]>> CountsAsync(TestOrder order) =>
        _database.RowsAsync(
            $"SELECT (SELECT COUNT(*) FROM `Orders` WHERE `text` = '{order.Text}'), (SELECT COUNT(*) FROM {_database.Table("Inbox")})", Ct);
}
