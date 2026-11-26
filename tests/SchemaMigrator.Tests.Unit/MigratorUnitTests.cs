using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using MsgHatch.Data.EFCore;

namespace SchemaMigrator.Tests.Unit;

/// <summary>
/// The schema migrator of the pipeline with no database (D320): the usage error, and the startup validation of its host.
/// It reads the connection string and the provider from its environment variables, so each test sets the variables of
/// the process and removes them at its end. The tests of this class run one by one. No test opens a connection, so the
/// connection strings name a closed port.
/// </summary>
public sealed class MigratorUnitTests : IDisposable
{
    private const string _connectionStringVariable = "SchemaMigrator__ConnectionString";

    private const string _providerVariable = "SchemaMigrator__Provider";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(_connectionStringVariable, null);
        Environment.SetEnvironmentVariable(_providerVariable, null);
    }

    [Theory]
    [InlineData("no connection string", null, null, new string[0])]
    [InlineData("unknown provider", "Server=localhost", "Oracle", new string[0])]
    [InlineData("unknown argument", "Server=localhost", null, new[] { "--unknown" })]
    [InlineData("script with no path", "Server=localhost", null, new[] { "--script" })]
    public async Task RunAsync_NoConnectionStringOrUnknownArguments_ReturnsTheUsageErrorAndChangesNothing(
        string row, string? connectionString, string? provider, string[] args)
    {
        // Arrange
        Environment.SetEnvironmentVariable(_connectionStringVariable, connectionString);
        Environment.SetEnvironmentVariable(_providerVariable, provider);
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
        using var host = Migrator.Build(Host.CreateApplicationBuilder(), "Server=127.0.0.1,1;Database=validation");

        // Act
        var exception = Record.Exception(host.Services.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    public void Build_PostgreSqlHostWithNoRoleAndNoMessageType_PassesTheStartupValidationOfMsgHatchOnPostgreSql()
    {
        // Arrange
        using var host = Migrator.Build(
            Host.CreateApplicationBuilder(), "Host=127.0.0.1;Port=1;Database=validation", SchemaMigratorOptions.PostgreSql);

        // Act
        var exception = Record.Exception(host.Services.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        using var context = host.Services.GetRequiredService<IDbContextFactory<MsgHatchDbContext>>().CreateDbContext();
        Assert.Equal((null, "Npgsql.EntityFrameworkCore.PostgreSQL"), (exception, context.Database.ProviderName));
    }
}
