using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MsgHatch.Data.EFCore.PostgreSql.Tests.Unit.TestTypes;

namespace MsgHatch.Data.EFCore.PostgreSql.Tests.Unit.DependencyInjection;

/// <summary>
/// <c>UseEntityFrameworkPostgreSqlStores</c> with no database (D318, D320): the startup validation, and the model has no pending
/// change against the migrations of the package. No test opens a connection, so the connection string names a closed
/// port.
/// </summary>
public sealed class EntityFrameworkPostgreSqlStoreExtensionsUnitTests
{
    private const string _connectionString = "Host=127.0.0.1;Port=1;Database=setup";

    public static TheoryData<string, string?> ModelRows => new()
    {
        { "msghatch", null },
        { "other", "POSIX" },
    };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task UseEntityFrameworkPostgreSqlStores_Alone_PassesTheStartupValidation()
    {
        // Arrange
        await using var provider = MigratedStores.Build(_connectionString);

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        Assert.Null(exception);
    }

    [Theory]
    [MemberData(nameof(ModelRows))]
    public async Task HasPendingModelChanges_SchemaAndCollationOfTheOptions_HasNoChangeAgainstTheMigrationsOfThePackage(string schema, string? collation)
    {
        // Arrange
        await using var provider = MigratedStores.Build(_connectionString, o => (o.Schema, o.TextCollation) = (schema, collation));
        await using var context = await provider.GetRequiredService<IDbContextFactory<MsgHatchDbContext>>().CreateDbContextAsync(Ct);

        // Act
        var hasChanges = context.Database.HasPendingModelChanges();

        // Assert
        Assert.False(hasChanges);
    }
}
