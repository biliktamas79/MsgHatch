using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Data.EFCore.DependencyInjection;
using MsgHatch.Data.EFCore.Providers;
using MsgHatch.Data.EFCore.Tests.Unit.TestTypes;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Tests.Unit.TestTypes;

namespace MsgHatch.Data.EFCore.Tests.Unit;

/// <summary>
/// The model of <see cref="MsgHatchDbContext"/> has no provider name: the collation of the text columns comes from
/// <c>TextCollation</c>, or else from the dialect, or else from the database (D318).
/// </summary>
public sealed class MsgHatchDbContextUnitTests
{
    private const string _connectionString = "Data Source=msghatch.db";

    public static TheoryData<string, string?, string?, string?> CollationRows => new()
    {
        { "no option and no dialect", null, null, null },
        { "the collation of the dialect", null, "NOCASE", "NOCASE" },
        { "the option over the dialect", "RTRIM", "NOCASE", "RTRIM" },
        { "the option with no dialect", "BINARY", null, "BINARY" },
    };

    public static TheoryData<string?, string> SchemaRows => new()
    {
        { null, "msghatch" },
        { "other", "other" },
    };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string[] TablesOf(DbContext context)
    {
        var history = RelationalOptionsExtension.Extract(context.GetService<IDbContextOptions>());
        return [.. context.GetService<IDesignTimeModel>().Model.GetEntityTypes().Select(static type => $"{type.GetSchema()}.{type.GetTableName()}")
            .Append($"{history.MigrationsHistoryTableSchema}.{history.MigrationsHistoryTableName}").Order(StringComparer.Ordinal)];
    }

    [Theory]
    [MemberData(nameof(CollationRows))]
    public async Task Model_TextCollation_IsTheCollationOfEachTextColumn(string row, string? option, string? dialectCollation, string? expected)
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatch().UseEntityFrameworkStores(
            o => o.UseSqlite(_connectionString).UseEntityFrameworkStoreDialect(new SqliteTestDialect(textCollation: dialectCollation)),
            o => o.TextCollation = option);
        await using var provider = services.Build();
        await using var context = await provider.GetRequiredService<IDbContextFactory<MsgHatchDbContext>>().CreateDbContextAsync(Ct);

        // Act
        var collations = context.GetService<IDesignTimeModel>().Model.GetEntityTypes().SelectMany(static type => type.GetProperties())
            .Where(static property => property.ClrType == typeof(string)).Select(static property => property.GetCollation()).Distinct();

        // Assert
        Assert.Equal((row, expected), (row, Assert.Single(collations)));
    }

    [Theory]
    [MemberData(nameof(SchemaRows))]
    public async Task Model_Schema_HoldsEachTableAndTheHistoryTableWithNoPrefix(string? option, string schema)
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatch().UseEntityFrameworkStores(o => o.UseSqlite(_connectionString), o => o.Schema = option ?? o.Schema);
        await using var provider = services.Build();
        await using var context = await provider.GetRequiredService<IDbContextFactory<MsgHatchDbContext>>().CreateDbContextAsync(Ct);

        // Act
        var tables = TablesOf(context);

        // Assert
        Assert.Equal(
            [$"{schema}.ChannelEntry", $"{schema}.ChannelState", $"{schema}.Inbox", $"{schema}.MessageTypeEntry", $"{schema}.Outbox", $"{schema}.__StoreMigrations"],
            tables);
    }

    [Fact]
    public async Task Model_IndexesAndKeys_HaveNamesWithNoPrefix()
    {
        // Arrange
        await using var context = await new SqliteTestDatabase().Contexts.CreateDbContextAsync(Ct);

        // Act
        var names = context.GetService<IDesignTimeModel>().Model.GetEntityTypes()
            .SelectMany(static type => type.GetIndexes().Select(static index => index.GetDatabaseName()).Concat(type.GetKeys().Select(static key => key.GetName())))
            .Order(StringComparer.Ordinal);

        // Assert
        Assert.Equal(
            ["IX_Inbox_Created", "IX_Inbox_Due", "IX_Inbox_Flags", "IX_Outbox_Created", "IX_Outbox_Due", "IX_Outbox_Flags", "IX_Outbox_Source",
                "IX_Outbox_Target", "PK_ChannelEntry", "PK_ChannelState", "PK_Inbox", "PK_MessageTypeEntry", "PK_Outbox", "UX_Inbox_Key", "UX_Outbox_Id"],
            names);
    }

    [Fact]
    public void PublicTypes_BasePackage_AreInTheRootTheSetupTheProviderAndTheMigrationNamespaces()
    {
        // Arrange
        var assembly = typeof(MsgHatchDbContext).Assembly;

        // Act
        var namespaces = assembly.GetExportedTypes().Select(static type => type.Namespace).Distinct().Order(StringComparer.Ordinal);

        // Assert
        Assert.Equal(
            ["MsgHatch.Data.EFCore", "MsgHatch.Data.EFCore.DependencyInjection", "MsgHatch.Data.EFCore.Migrations", "MsgHatch.Data.EFCore.Providers"],
            namespaces);
    }

    [Fact]
    public async Task Model_DueIndexes_HaveNoFilterAndTheStatusAsAColumn()
    {
        // Arrange
        await using var context = await new SqliteTestDatabase().Contexts.CreateDbContextAsync(Ct);

        // Act
        var indexes = context.Model.GetEntityTypes().SelectMany(static type => type.GetIndexes())
            .Where(static index => index.GetDatabaseName()!.EndsWith("_Due", StringComparison.Ordinal))
            .Select(static index => (index.GetDatabaseName(), index.GetFilter(), string.Join(',', index.Properties.Select(static property => property.Name))))
            .Order();

        // Assert
        Assert.Equal(
            [("IX_Inbox_Due", null, "Status,NextTryAtUtc,Sequence"), ("IX_Outbox_Due", null, "Channel,Status,NextTryAtUtc,Sequence")],
            indexes);
    }
}
