using MsgHatch.Data.EFCore.Tests.Integration.TestTypes;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Management;
using MsgHatch.Domain.Persistence;
using MsgHatch.Domain.Tests.Unit.Contracts;
using MsgHatch.Domain.Tests.Unit.TestTypes;

namespace MsgHatch.Data.EFCore.Tests.Integration;

public sealed class EntityFrameworkInboxStoreIntegTests(SqlServerFixture server)
    : InboxStoreTestsBase<EntityFrameworkInboxStore>(
        new EntityFrameworkInboxStore(server.Database("inbox").Contexts, new MsgHatchJson(TestContentTypes.ARegistry())),
        static (store, key) => store.For(key),
        _ => new EntityFrameworkInboxStoreTestData(server.Database("inbox")))
{
    private const string _isoTime = "2030-01-01T00:00:00.0000000Z";

    private TestDatabase Database => server.Database("inbox");

    [Fact]
    public async Task AddAsync_SameIdWithNoTenantAndATenant_WritesTwoRowsWithTheTenantAndTheEnumValuesAsText()
    {
        // Arrange
        var ofNoTenant = await AddAsync(AMessage(TenantKey.None));
        var ofT1 = AMessage(T1, id: ofNoTenant.Delivery.Id, flags: MessageFlags.Test);
        var id = ofNoTenant.Delivery.Id.ToString("D");

        // Act
        await TenantStore(T1).AddAsync(ofT1, Ct);

        // Assert
        Assert.Equal(
            [["", id, "Pending", _isoTime, "None"], ["t1", id, "Pending", _isoTime, "Test"]],
            await Database.RowsAsync("SELECT TenantId, Id, Status, NextTryAtUtc, Flags FROM [msghatch].[Inbox] ORDER BY Sequence", Ct));
    }

    [Fact]
    public async Task QueryAsync_CompletedMessage_GivesEachTimeWithTheKindUtc()
    {
        // Arrange
        var message = await AddAsync(AMessage(T1));
        await Store.CompleteAsync(message, Now, Ct);

        // Act
        var page = await TenantStore(T1).QueryAsync(new InboxQuery(), Ct);

        // Assert
        var delivery = Assert.Single(page.Value.Items).Delivery;
        Assert.Equal([DateTimeKind.Utc, DateTimeKind.Utc, DateTimeKind.Utc], [delivery.CreatedAtUtc.Kind, delivery.NextTryAtUtc.Kind, delivery.LastTryAtUtc!.Value.Kind]);
    }
}
