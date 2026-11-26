using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MsgHatch.Data.EFCore.PostgreSql.Tests.Unit.TestTypes;

namespace MsgHatch.Data.EFCore.PostgreSql.Tests.Unit.DependencyInjection;

/// <summary>
/// <c>UseEntityFrameworkPostgreSqlStores&lt;TContext&gt;</c> (D331): the startup validation compares the server and the database of
/// the context of the host and of the store contexts from their connection strings, and opens no connection.
/// </summary>
public sealed partial class EntityFrameworkPostgreSqlStoreExtensionsUnitTests
{
    public static TheoryData<string, string> OtherDatabaseRows => new()
    {
        { "another database on the server", "Host=127.0.0.1;Port=1;Database=other" },
        { "the database on another server", "Host=127.0.0.2;Port=1;Database=setup" },
    };

    [Fact]
    public async Task UseEntityFrameworkPostgreSqlStoresOfTheHost_ContextOnTheDatabaseOfTheStores_PassesTheStartupValidation()
    {
        // Arrange
        await using var provider = MigratedStores.BuildWithHostContext(_connectionString, _connectionString);

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        Assert.Null(exception);
    }

    [Theory]
    [MemberData(nameof(OtherDatabaseRows))]
    public async Task UseEntityFrameworkPostgreSqlStoresOfTheHost_ContextOnAnotherDatabase_FailsTheStartupValidation(string row, string hostConnectionString)
    {
        // Arrange
        await using var provider = MigratedStores.BuildWithHostContext(_connectionString, hostConnectionString);

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        Assert.StartsWith(
            $"{row}: UseEntityFrameworkStores<TestHostContext> needs TestHostContext and the store contexts on one database",
            $"{row}: {Assert.Single(Assert.IsType<OptionsValidationException>(exception).Failures)}",
            StringComparison.Ordinal);
    }
}
