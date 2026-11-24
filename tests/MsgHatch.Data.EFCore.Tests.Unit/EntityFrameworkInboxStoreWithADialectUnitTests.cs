using MsgHatch.Data.EFCore.Tests.Unit.TestTypes;
using MsgHatch.Domain.Persistence;
using MsgHatch.Domain.Tests.Unit.Contracts;
using MsgHatch.Domain.Tests.Unit.TestTypes;

namespace MsgHatch.Data.EFCore.Tests.Unit;

/// <summary>The inbox contract on SQLite with the claim statement of a dialect (D318).</summary>
public sealed class EntityFrameworkInboxStoreWithADialectUnitTests : InboxStoreTestsBase<EntityFrameworkInboxStore>
{
    private readonly SqliteTestDialect _dialect;

    public EntityFrameworkInboxStoreWithADialectUnitTests()
        : this(new SqliteTestDatabase(), new SqliteTestDialect())
    {
    }

    private EntityFrameworkInboxStoreWithADialectUnitTests(SqliteTestDatabase database, SqliteTestDialect dialect)
        : base(
            new EntityFrameworkInboxStore(
                database.ContextsOf<MsgHatchDbContext>(o => o.UseEntityFrameworkStoreDialect(dialect)), new MsgHatchJson(TestContentTypes.ARegistry())),
            static (store, key) => store.For(key),
            _ => new EntityFrameworkInboxStoreTestData(database))
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
}
