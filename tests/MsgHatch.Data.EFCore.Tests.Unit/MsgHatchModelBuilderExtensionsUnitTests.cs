using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using MsgHatch.Data.EFCore.Providers;
using MsgHatch.Data.EFCore.Tests.Unit.TestTypes;
using MsgHatch.Tests.Shared;

namespace MsgHatch.Data.EFCore.Tests.Unit;

/// <summary>
/// <c>MapMsgHatchInbox</c> on the EF Core SQLite provider (D331): the inbox table of a context of the host equals the inbox
/// table of <see cref="MsgHatchDbContext"/>, and the tables, the conventions, and the migrations of the host do not change.
/// The model needs no connection.
/// </summary>
public sealed class MsgHatchModelBuilderExtensionsUnitTests
{
    private const string _connectionString = "Data Source=msghatch-host.db";

    public static TheoryData<string, string?, string?, bool> OptionRows => new()
    {
        { "a dialect with no collation and no JSON type", null, null, true },
        { "a dialect with a collation and a JSON type", "NOCASE", "json", true },
        { "a dialect of a database with no schemas", null, null, false },
    };

    [Theory]
    [MemberData(nameof(OptionRows))]
    public async Task MapMsgHatchInbox_OptionsOfTheContexts_GivesTheInboxTableOfMsgHatchDbContext(
        string row, string? textCollation, string? jsonColumnType, bool hasSchemas)
    {
        // Arrange
        var dialect = new SqliteTestDialect(textCollation: textCollation, jsonColumnType: jsonColumnType, hasSchemas: hasSchemas);
        await using var store = new MsgHatchDbContext(OptionsOf<MsgHatchDbContext>(dialect));
        string[] expected = [row, .. TableDescriptions.Of(store, "Inbox")];
        await using var host = new TestHostContext(OptionsOf<TestHostContext>(dialect));

        // Act
        string[] table = [row, .. TableDescriptions.Of(host, "Inbox")];

        // Assert
        Assert.Equal(expected, table);
    }

    [Fact]
    public async Task MapMsgHatchInbox_DialectWithACollation_KeepsTheSchemaTheColumnsAndTheConventionsOfTheHostTable()
    {
        // Arrange
        await using var host = new TestHostContext(OptionsOf<TestHostContext>(new SqliteTestDialect(textCollation: "NOCASE")));

        // Act
        var orders = TableDescriptions.Of(host, "Orders");

        // Assert
        Assert.Equal(
            [
                "table .Orders", "column id INTEGER not null   Sqlite:Autoincrement=True", "column createdatutc TEXT not null   ",
                "column text TEXT not null   ", "key id", "annotations ",
            ],
            orders);
    }

    [Fact]
    public async Task MapMsgHatchInbox_ModelOfTheHost_GivesAMigrationOfTheHostTableOnly()
    {
        // Arrange
        await using var host = new TestHostContext(OptionsOf<TestHostContext>(new SqliteTestDialect()));
        var model = host.GetService<IDesignTimeModel>().Model.GetRelationalModel();

        // Act
        var operations = host.GetService<IMigrationsModelDiffer>().GetDifferences(source: null, model);

        // Assert
        Assert.Equal(
            ["CreateTableOperation Orders"],
            operations.Select(static operation => operation switch
            {
                CreateTableOperation table => $"{nameof(CreateTableOperation)} {table.Name}",
                CreateIndexOperation index => $"{nameof(CreateIndexOperation)} {index.Name}",
                EnsureSchemaOperation schema => $"{nameof(EnsureSchemaOperation)} {schema.Name}",
                _ => operation.GetType().Name,
            }));
    }

    private static DbContextOptions<TContext> OptionsOf<TContext>(IEntityFrameworkStoreDialect dialect)
        where TContext : DbContext
    {
        var options = new DbContextOptionsBuilder<TContext>().UseSqlite(_connectionString);
        options.IgnoreManyServiceProvidersWarning();
        options.UseEntityFrameworkStoreDialect(dialect);
        return options.Options;
    }
}
