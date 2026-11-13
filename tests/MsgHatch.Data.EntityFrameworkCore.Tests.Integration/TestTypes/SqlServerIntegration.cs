using Microsoft.Extensions.Configuration;

namespace MsgHatch.Data.EntityFrameworkCore.Tests.Integration.TestTypes;

/// <summary>
/// The SQL Server of the EF Core integration tests, from the section <c>SqlServerIntegration</c> of
/// <c>appsettings.json</c> and of <c>appsettings.Local.json</c>, which git ignores (D180).
/// </summary>
public static class SqlServerIntegration
{
    public static SqlServerIntegrationSettings Settings { get; } = Load();

    /// <summary>Whether a server is set. With no server, the tests start SQL Server with Testcontainers (D124).</summary>
    public static bool IsConfigured => !string.IsNullOrWhiteSpace(Settings.ConnectionString);

    private static SqlServerIntegrationSettings Load() =>
        new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json")
            .AddJsonFile("appsettings.Local.json", optional: true)
            .Build()
            .GetSection(SqlServerIntegrationSettings.SectionName)
            .Get<SqlServerIntegrationSettings>() ?? new SqlServerIntegrationSettings();
}

/// <summary>The settings of the SQL Server of the integration tests. <c>appsettings.json</c> describes each value.</summary>
public sealed class SqlServerIntegrationSettings
{
    public const string SectionName = "SqlServerIntegration";

    public string? ConnectionString { get; set; }
}
