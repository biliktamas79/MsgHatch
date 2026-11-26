using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MsgHatch.Data.EFCore.MySql.Tests.Unit.TestTypes;

namespace MsgHatch.Data.EFCore.MySql.Tests.Unit.DependencyInjection;

/// <summary>
/// <c>UseEntityFrameworkMySqlStores</c> with no database (D318, D320): the startup validation, and the model has no pending
/// change against the migrations of the package. No test opens a connection, so the connection string names a closed
/// port.
/// </summary>
public sealed class EntityFrameworkMySqlStoreExtensionsUnitTests
{
    private const string _connectionString = "Server=127.0.0.1;Port=1;Database=setup";

    public static TheoryData<string?, string?> ModelRows => new()
    {
        { null, null },
        { "utf8mb4_0900_as_cs", null },
        { null, "json" },
    };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task UseEntityFrameworkMySqlStores_Alone_PassesTheStartupValidation()
    {
        // Arrange
        await using var provider = MigratedStores.Build(_connectionString);

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    public async Task UseEntityFrameworkMySqlStores_SchemaThatIsNotTheDefault_FailsTheStartupValidation()
    {
        // Arrange
        await using var provider = MigratedStores.Build(_connectionString, static o => o.Schema = "other");

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        Assert.Contains("Schema", Assert.Single(Assert.IsType<OptionsValidationException>(exception).Failures), StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(ModelRows))]
    public async Task HasPendingModelChanges_CollationAndJsonTypeOfTheOptions_HasNoChangeAgainstTheMigrationsOfThePackage(string? collation, string? json)
    {
        // Arrange
        await using var provider = MigratedStores.Build(
            _connectionString, o => (o.TextCollation, o.JsonColumnType) = (collation, json));
        await using var context = await provider.GetRequiredService<IDbContextFactory<MsgHatchDbContext>>().CreateDbContextAsync(Ct);

        // Act
        var hasChanges = context.Database.HasPendingModelChanges();

        // Assert
        Assert.False(hasChanges);
    }
}
