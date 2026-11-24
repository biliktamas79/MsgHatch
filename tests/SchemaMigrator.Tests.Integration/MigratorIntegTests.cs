using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using MsgHatch.Data.EFCore.Tests.Integration.TestTypes;

[assembly: AssemblyFixture(typeof(SqlServerFixture))]

namespace SchemaMigrator.Tests.Integration;

/// <summary>
/// The schema migrator of the pipeline on SQL Server (D320). It reads the connection string from its environment
/// variable, so each test sets the variable of the process and removes it at its end. The tests of this class run one by
/// one.
/// </summary>
public sealed class MigratorIntegTests(SqlServerFixture server) : IDisposable
{
    private const string _connectionStringVariable = "SchemaMigrator__ConnectionString";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => Environment.SetEnvironmentVariable(_connectionStringVariable, null);

    [Fact]
    public async Task RunAsync_ConnectionStringOfTheEnvironmentVariable_AppliesTheMigrationsOfBothContextsToTheEmptyDatabase()
    {
        // Arrange
        var database = server.Database("migrate");
        await database.CreateEmptyAsync(Ct);
        Environment.SetEnvironmentVariable(_connectionStringVariable, database.ConnectionString);
        using var output = new StringWriter();

        // Act
        var exitCode = await Migrator.RunAsync([], output, Ct);

        // Assert
        Assert.Equal(
            (Migrator.Success, "Applied the MsgHatch migrations.", 8),
            (exitCode, output.ToString().Trim(), (await database.RowsAsync("SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES", Ct)).Count));
    }

    [Fact]
    public async Task RunAsync_ScriptArgument_WritesTheIdempotentScriptOfBothContextsAndChangesNoDatabase()
    {
        // Arrange
        var database = server.Database("script");
        await database.CreateEmptyAsync(Ct);
        Environment.SetEnvironmentVariable(_connectionStringVariable, database.ConnectionString);
        var path = Path.Combine(Path.GetTempPath(), $"msghatch-migrations-{Guid.NewGuid():N}.sql");
        using var output = new StringWriter();

        // Act
        var exitCode = await Migrator.RunAsync(["--script", path], output, Ct);

        // Assert
        var script = await File.ReadAllTextAsync(path, Ct);
        File.Delete(path);
        Assert.Equal(
            (Migrator.Success, true, true, 0),
            (exitCode, script.Contains("[__MsgHatchMigrations]", StringComparison.Ordinal),
                script.Contains("[__MsgHatchSubscriptionMigrations]", StringComparison.Ordinal),
                (await database.RowsAsync("SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES", Ct)).Count));
    }

    [Theory]
    [InlineData("no connection string", null, new string[0])]
    [InlineData("unknown argument", "Server=localhost", new[] { "--unknown" })]
    [InlineData("script with no path", "Server=localhost", new[] { "--script" })]
    public async Task RunAsync_NoConnectionStringOrUnknownArguments_ReturnsTheUsageErrorAndChangesNothing(string row, string? connectionString, string[] args)
    {
        // Arrange
        Environment.SetEnvironmentVariable(_connectionStringVariable, connectionString);
        using var output = new StringWriter();

        // Act
        var exitCode = await Migrator.RunAsync(args, output, Ct);

        // Assert
        Assert.Equal((row, Migrator.UsageError, true), (row, exitCode, output.ToString().StartsWith("Usage: SchemaMigrator", StringComparison.Ordinal)));
    }

    [Fact]
    public void Build_HostWithNoRoleAndNoMessageType_PassesTheStartupValidationOfMsgHatch()
    {
        // Arrange
        using var host = Migrator.Build(Host.CreateApplicationBuilder(), server.Database("validation").ConnectionString);

        // Act
        var exception = Record.Exception(host.Services.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        Assert.Null(exception);
    }
}
