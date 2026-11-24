using MsgHatch.Data.EFCore.Tests.Unit.TestTypes;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Persistence;
using MsgHatch.Domain.Tests.Unit.Contracts;
using MsgHatch.Domain.Tests.Unit.TestTypes;

namespace MsgHatch.Data.EFCore.Tests.Unit;

/// <summary>The inbox contract on the EF Core SQLite provider, which has no claim statement, so the store claims with LINQ (D318).</summary>
public sealed class EntityFrameworkInboxStoreUnitTests : InboxStoreTestsBase<EntityFrameworkInboxStore>
{
    private readonly SqliteTestDatabase _database;

    public EntityFrameworkInboxStoreUnitTests()
        : this(new SqliteTestDatabase())
    {
    }

    private EntityFrameworkInboxStoreUnitTests(SqliteTestDatabase database)
        : base(
            new EntityFrameworkInboxStore(database.Contexts, new MsgHatchJson(TestContentTypes.ARegistry())),
            static (store, key) => store.For(key),
            _ => new EntityFrameworkInboxStoreTestData(database))
    {
        _database = database;
    }

    [Fact]
    public async Task AddAsync_SameIdWithNoTenantAndATenant_WritesTwoNumberedRowsWithTheTenantAndTheEnumValuesAsText()
    {
        // Arrange
        var ofNoTenant = await AddAsync(AMessage(TenantKey.None));
        var ofT1 = AMessage(T1, id: ofNoTenant.Delivery.Id, flags: MessageFlags.Test);
        var id = ofNoTenant.Delivery.Id.ToString("D");

        // Act
        await TenantStore(T1).AddAsync(ofT1, Ct);

        // Assert
        Assert.Equal(
            [["1", "", id, "Pending", "2030-01-01 00:00:00", "None"], ["2", "t1", id, "Pending", "2030-01-01 00:00:00", "Test"]],
            _database.Rows("SELECT Sequence, TenantId, Id, Status, NextTryAtUtc, Flags FROM MsgHatchInbox ORDER BY Sequence"));
    }

    [Fact]
    public async Task ClaimDueAsync_RowWithAnEventThatIsNotJson_FailsTheRowAndClaimsTheOtherMessage()
    {
        // Arrange
        var unreadable = await AddAsync(AMessage(T1));
        var message = await AddAsync(AMessage(T1));
        _database.Execute($"UPDATE MsgHatchInbox SET Content = '{{ not json' WHERE Id = '{unreadable.Delivery.Id:D}'");

        // Act
        var result = await ClaimAsync();

        // Assert
        Assert.Equal(message.Delivery.Id, Assert.Single(result.Value.Messages).Delivery.Id);
        Assert.Equal(
            ["Failed", "SerializationFailed", null],
            Assert.Single(_database.Rows(
                $"SELECT Status, substr(LastError, 1, instr(LastError, ':') - 1), LeaseUntilUtc FROM MsgHatchInbox WHERE Id = '{unreadable.Delivery.Id:D}'")));
    }

    [Fact]
    public async Task ClaimDueAsync_FirstCandidatesThatAnotherDispatcherTookFirst_ClaimsTheNextDueMessages()
    {
        // Arrange
        var interceptor = new FirstClaimUpdateInterceptor(_database, "UPDATE MsgHatchInbox SET NextTryAtUtc = '2030-01-01 00:00:30' WHERE Sequence <= 2");
        var store = new EntityFrameworkInboxStore(
            _database.ContextsOf<MsgHatchDbContext>(o => o.AddInterceptors(interceptor)), new MsgHatchJson(TestContentTypes.ARegistry()));
        var messages = new[] { AMessage(T1), AMessage(T1), AMessage(T1), AMessage(T1) };
        foreach (var message in messages)
        {
            await AddAsync(message);
        }

        // Act
        var result = await store.ClaimDueAsync(2, Now, TimeSpan.FromSeconds(30), Ct);

        // Assert
        Assert.Equal(
            [messages[2].Delivery.Id, messages[3].Delivery.Id],
            result.Value.Messages.Select(static message => message.Delivery.Id));
    }

    [Fact]
    public async Task AddAsync_TimesWithNoKind_SendsEachTimeWithTheKindUtc()
    {
        // Arrange
        var interceptor = new TimeParameterInterceptor();
        var store = new EntityFrameworkInboxStore(
            _database.ContextsOf<MsgHatchDbContext>(o => o.AddInterceptors(interceptor)), new MsgHatchJson(TestContentTypes.ARegistry()));
        var message = AMessage(T1);
        var noKind = DateTime.SpecifyKind(Now, DateTimeKind.Unspecified);

        // Act
        await store.For(T1).AddAsync(message with { Delivery = message.Delivery with { CreatedAtUtc = noKind, NextTryAtUtc = noKind } }, Ct);

        // Assert
        Assert.Equal([DateTimeKind.Utc], interceptor.Kinds.Distinct());
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
    public async Task AddAsync_TableThatDoesNotExist_ReturnsUnavailable()
    {
        // Arrange
        _database.Execute("DROP TABLE MsgHatchInbox");

        // Act
        var error = await TenantStore(T1).AddAsync(AMessage(T1), Ct);

        // Assert
        Assert.Equal(Errors.Unavailable, error?.Type);
    }
}
