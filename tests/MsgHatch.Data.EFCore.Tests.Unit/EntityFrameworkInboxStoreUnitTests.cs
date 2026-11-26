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
            _database.Rows("SELECT Sequence, TenantId, Id, Status, NextTryAtUtc, Flags FROM Inbox ORDER BY Sequence"));
    }

    [Fact]
    public async Task ClaimDueAsync_RowWithAnEventThatIsNotJson_FailsTheRowAndClaimsTheOtherMessage()
    {
        // Arrange
        var unreadable = await AddAsync(AMessage(T1));
        var message = await AddAsync(AMessage(T1));
        _database.Execute($"UPDATE Inbox SET Content = '{{ not json' WHERE Id = '{unreadable.Delivery.Id:D}'");

        // Act
        var result = await ClaimAsync();

        // Assert
        Assert.Equal(message.Delivery.Id, Assert.Single(result.Value.Messages).Delivery.Id);
        Assert.Equal(
            ["Failed", "SerializationFailed", null],
            Assert.Single(_database.Rows(
                $"SELECT Status, substr(LastError, 1, instr(LastError, ':') - 1), LeaseUntilUtc FROM Inbox WHERE Id = '{unreadable.Delivery.Id:D}'")));
    }

    [Fact]
    public async Task ClaimDueAsync_EventThatAddsAPropertyToTheDocument_FailsTheRowAndKeepsItsTenant()
    {
        // Arrange
        var unreadable = await AddAsync(AMessage(T1));
        _database.Execute($$"""UPDATE Inbox SET Content = '{"text":"x"}, "tenant": "t2"' WHERE Id = '{{unreadable.Delivery.Id:D}}'""");

        // Act
        var result = await ClaimAsync();

        // Assert
        Assert.Empty(result.Value.Messages);
        Assert.Equal(["t1", "Failed"], Assert.Single(_database.Rows("SELECT TenantId, Status FROM Inbox")));
    }

    [Fact]
    public async Task ClaimDueAsync_FirstCandidatesThatAnotherDispatcherTookFirst_ClaimsTheNextDueMessages()
    {
        // Arrange
        var interceptor = new FirstClaimUpdateInterceptor(_database, "UPDATE Inbox SET NextTryAtUtc = '2030-01-01 00:00:30' WHERE Sequence <= 2");
        var store = new EntityFrameworkInboxStore(
            _database.ContextsOf<MsgHatchDbContext>(o => o.AddInterceptors(interceptor)), new MsgHatchJson(TestContentTypes.ARegistry()));
        var messages = new[]
        {
            await AddAsync(AMessage(T1)), await AddAsync(AMessage(T1)), await AddAsync(AMessage(T1)), await AddAsync(AMessage(T1)),
        };

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
        using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(Ct);
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
        var missingDatabase = new FailingConnectionInterceptor();
        var store = new EntityFrameworkInboxStore(
            new SqliteTestDatabase().ContextsOf<MsgHatchDbContext>(o => o.AddInterceptors(missingDatabase)), new MsgHatchJson(TestContentTypes.ARegistry()));

        // Act
        var error = await store.For(T1).AddAsync(AMessage(T1), Ct);

        // Assert
        Assert.Equal((Errors.Unavailable, 1), (error?.Type, missingDatabase.Opens));
    }

    [Fact]
    public async Task AddAsync_InsertThatFailsWithNoStoredKey_ReturnsUnavailableAndAddsNothing()
    {
        // Arrange
        _database.RefuseInserts("Inbox");

        // Act
        var error = await TenantStore(T1).AddAsync(AMessage(T1), Ct);

        // Assert
        Assert.Equal(Errors.Unavailable, error?.Type);
        Assert.Empty(await TestData.GetStoredAsync(Ct));
    }

    [Fact]
    public async Task ClaimDueAsync_ConnectionThatThrowsAnExceptionOfNoDatabase_ThrowsItToTheCaller()
    {
        // Arrange
        var failure = new NotSupportedException("not a database failure");
        var store = new EntityFrameworkInboxStore(
            _database.ContextsOf<MsgHatchDbContext>(o => o.AddInterceptors(new FailingConnectionInterceptor(failure))),
            new MsgHatchJson(TestContentTypes.ARegistry()));

        // Act
        var exception = await Record.ExceptionAsync(async () => await store.ClaimDueAsync(10, Now, TimeSpan.FromSeconds(30), Ct));

        // Assert
        Assert.Same(failure, exception);
    }

    [Fact]
    public async Task AddAsync_TableThatDoesNotExist_ReturnsUnavailable()
    {
        // Arrange
        _database.Execute("DROP TABLE Inbox");

        // Act
        var error = await TenantStore(T1).AddAsync(AMessage(T1), Ct);

        // Assert
        Assert.Equal(Errors.Unavailable, error?.Type);
    }
}
