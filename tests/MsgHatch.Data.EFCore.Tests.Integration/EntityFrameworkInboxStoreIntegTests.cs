using MsgHatch.Data.EFCore.Tests.Integration.TestTypes;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
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
    public async Task ClaimDueAsync_CancelledToken_GivesOperationCancelled()
    {
        // Arrange
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        // Act
        var result = await Store.ClaimDueAsync(10, Now, TimeSpan.FromSeconds(30), cancelled.Token);

        // Assert
        Assert.Equal(Errors.OperationCancelled, result.Error?.Type);
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

    [Fact]
    public async Task AddAsync_DatabaseThatDoesNotExist_ReturnsUnavailable()
    {
        // Arrange
        var store = new EntityFrameworkInboxStore(server.Database("missing").Contexts, new MsgHatchJson(TestContentTypes.ARegistry()));

        // Act
        var error = await store.For(T1).AddAsync(AMessage(T1), Ct);

        // Assert
        Assert.Equal(Errors.Unavailable, error?.Type);
    }

    [Fact]
    public async Task ClaimDueAsync_RowWithAnEventThatIsNotJson_FailsTheRowAndClaimsTheOtherMessage()
    {
        // Arrange
        var unreadable = await AddAsync(AMessage(T1));
        var message = await AddAsync(AMessage(T1));
        await Database.ExecuteAsync($"UPDATE [msghatch].[Inbox] SET Content = '{{ not json' WHERE Id = '{unreadable.Delivery.Id:D}'", Ct);

        // Act
        var result = await ClaimAsync();

        // Assert
        Assert.Equal(message.Delivery.Id, Assert.Single(result.Value.Messages).Delivery.Id);
        Assert.Equal(
            ["Failed", "SerializationFailed", null],
            Assert.Single(await Database.RowsAsync(
                $"SELECT Status, LEFT(LastError, CHARINDEX(':', LastError) - 1), LeaseUntilUtc FROM [msghatch].[Inbox] WHERE Id = '{unreadable.Delivery.Id:D}'",
                Ct)));
    }

    [Fact]
    public async Task ClaimDueAsync_EventThatAddsAPropertyToTheDocument_FailsTheRowAndKeepsItsTenant()
    {
        // Arrange
        var unreadable = await AddAsync(AMessage(T1));
        await Database.ExecuteAsync(
            $$"""UPDATE [msghatch].[Inbox] SET Content = '{"text":"x"}, "tenant": "t2"' WHERE Id = '{{unreadable.Delivery.Id:D}}'""", Ct);

        // Act
        var result = await ClaimAsync();

        // Assert
        Assert.Empty(result.Value.Messages);
        Assert.Equal(["t1", "Failed"], Assert.Single(await Database.RowsAsync("SELECT TenantId, Status FROM [msghatch].[Inbox]", Ct)));
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
