using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Data.EFCore.DependencyInjection;
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

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

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
            [("IX_MsgHatchInbox_Due", null, "Status,NextTryAtUtc,Sequence"), ("IX_MsgHatchOutbox_Due", null, "Channel,Status,NextTryAtUtc,Sequence")],
            indexes);
    }
}
