using Microsoft.Data.Sqlite;
using MsgHatch.Data.Sqlite.Tests.Unit.TestTypes;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Persistence;
using MsgHatch.Domain.Tests.Unit.Contracts;
using MsgHatch.Domain.Tests.Unit.TestTypes;

namespace MsgHatch.Data.Sqlite.Tests.Unit;

public sealed class SqliteInboxStoreUnitTests()
    : InboxStoreTestsBase<SqliteInboxStore>(
        new SqliteInboxStore(TestDatabases.NewOptions().ConnectionString, new MsgHatchJson(TestContentTypes.ARegistry())),
        static (store, key) => store.For(key),
        static store => new SqliteInboxStoreTestData(store))
{
    [Fact]
    public async Task AddAsync_SameIdWithNoTenantAndATenant_WritesTwoNumberedRowsWithTheTenantTheTimeAndTheEnumValuesAsText()
    {
        // Arrange
        var ofNoTenant = await AddAsync(AMessage(TenantKey.None));
        var ofT1 = AMessage(T1, id: ofNoTenant.Delivery.Id, flags: MessageFlags.Test);
        var id = ofNoTenant.Delivery.Id.ToString("D");

        // Act
        await TenantStore(T1).AddAsync(ofT1, Ct);

        // Assert
        Assert.Equal(
            [["1", "", id, "Pending", "2030-01-01T00:00:00.0000000Z", "None"], ["2", "t1", id, "Pending", "2030-01-01T00:00:00.0000000Z", "Test"]],
            TestDatabases.Rows(Store.ConnectionString, "SELECT Sequence, TenantId, Id, Status, NextTryAtUtc, Flags FROM MsgHatchInbox ORDER BY Sequence"));
    }

    [Fact]
    public async Task ClaimDueAsync_RowWithAnEventThatIsNotJson_FailsTheRowAndClaimsTheOtherMessage()
    {
        // Arrange
        var unreadable = await AddAsync(AMessage(T1));
        var message = await AddAsync(AMessage(T1));
        TestDatabases.Execute(Store.ConnectionString, $"UPDATE MsgHatchInbox SET Content = '{{ not json' WHERE Id = '{unreadable.Delivery.Id:D}'");

        // Act
        var result = await ClaimAsync();

        // Assert
        Assert.Equal(message.Delivery.Id, Assert.Single(result.Value.Messages).Delivery.Id);
        Assert.Equal(
            ["Failed", "SerializationFailed", null],
            Assert.Single(TestDatabases.Rows(
                Store.ConnectionString,
                $"SELECT Status, substr(LastError, 1, instr(LastError, ':') - 1), LeaseUntilUtc FROM MsgHatchInbox WHERE Id = '{unreadable.Delivery.Id:D}'")));
    }

    [Fact]
    public async Task AddAsync_DatabaseInAFolderThatDoesNotExist_ReturnsUnavailable()
    {
        // Arrange
        var missing = Path.Combine(Path.GetTempPath(), "msghatch-tests", Guid.NewGuid().ToString("N"), "msghatch.db");
        var store = new SqliteInboxStore(
            new SqliteConnectionStringBuilder(Store.ConnectionString) { DataSource = missing }.ToString(), new MsgHatchJson(TestContentTypes.ARegistry()));

        // Act
        var error = await store.For(T1).AddAsync(AMessage(T1), Ct);

        // Assert
        Assert.Equal(Errors.Unavailable, error?.Type);
    }

    [Fact]
    public async Task AddAsync_MessageOfAnUnregisteredType_ReturnsSerializationFailedAndAddsNothing()
    {
        // Arrange
        var message = AMessage(T1) with { Type = new MessageType("Unknown") };

        // Act
        var error = await TenantStore(T1).AddAsync(message, Ct);

        // Assert
        Assert.Equal(Errors.SerializationFailed, error?.Type);
        Assert.Empty(await TestData.GetStoredAsync(Ct));
    }
}
