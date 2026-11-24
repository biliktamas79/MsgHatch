using MsgHatch.Data.EFCore.Tests.Unit.TestTypes;
using MsgHatch.Domain.Persistence;
using MsgHatch.Domain.Tests.Unit.Contracts;
using MsgHatch.Domain.Tests.Unit.TestTypes;

namespace MsgHatch.Data.EFCore.Tests.Unit;

/// <summary>The outbox contract on SQLite with the claim statement of a dialect (D318).</summary>
public sealed class EntityFrameworkOutboxStoreWithADialectUnitTests : OutboxStoreTestsBase<EntityFrameworkOutboxStore>
{
    private readonly SqliteTestDialect _dialect;

    public EntityFrameworkOutboxStoreWithADialectUnitTests()
        : this(new SqliteTestDatabase(), new SqliteTestDialect())
    {
    }

    private EntityFrameworkOutboxStoreWithADialectUnitTests(SqliteTestDatabase database, SqliteTestDialect dialect)
        : base(
            new EntityFrameworkOutboxStore(
                database.ContextsOf<MsgHatchDbContext>(o => o.UseEntityFrameworkStoreDialect(dialect)), new MsgHatchJson(TestContentTypes.ARegistry())),
            static (store, key) => store.For(key),
            _ => new EntityFrameworkOutboxStoreTestData(database))
    {
        _dialect = dialect;
    }

    [Fact]
    public async Task ClaimDueAsync_DueMessage_ClaimsItWithTheStatementOfTheDialect()
    {
        // Arrange
        var message = await AddAsync(AMessage(T1));

        // Act
        var result = await ClaimAsync();

        // Assert
        Assert.Equal((message.Delivery.Id, 1), (Assert.Single(result.Value.Messages).Delivery.Id, _dialect.ClaimStatements));
    }

    [Fact]
    public async Task ClaimDueAsync_DialectWithNoClaimStatement_ClaimsWithLinq()
    {
        // Arrange
        var database = new SqliteTestDatabase();
        var store = new EntityFrameworkOutboxStore(
            database.ContextsOf<MsgHatchDbContext>(o => o.UseEntityFrameworkStoreDialect(new SqliteTestDialect(hasClaimStatement: false))),
            new MsgHatchJson(TestContentTypes.ARegistry()));
        await database.ResetAsync(database.Contexts, Ct);
        var message = AMessage(T1);
        await store.For(T1).AddAsync([message], Ct);

        // Act
        var result = await store.ClaimDueAsync(TestChannel.Id, 10, Now, TimeSpan.FromSeconds(30), Ct);

        // Assert
        Assert.Equal(message.Delivery.Id, Assert.Single(result.Value.Messages).Delivery.Id);
        database.Delete();
    }
}
