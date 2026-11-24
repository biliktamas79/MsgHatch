using Microsoft.Extensions.Configuration;

namespace MsgHatch.Data.EFCore.Tests.Integration.TestTypes;

/// <summary>
/// The PostgreSQL of the EF Core integration tests, from the section <c>PostgreSqlIntegration</c> of
/// <c>appsettings.json</c> and of <c>appsettings.Local.json</c>, which git ignores (D180).
/// </summary>
public static class PostgreSqlIntegration
{
    public static PostgreSqlIntegrationSettings Settings { get; } = Load();

    /// <summary>Whether a server is set. With no server, the tests start PostgreSQL with Testcontainers (D124).</summary>
    public static bool IsConfigured => !string.IsNullOrWhiteSpace(Settings.ConnectionString);

    private static PostgreSqlIntegrationSettings Load() =>
        new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json")
            .AddJsonFile("appsettings.Local.json", optional: true)
            .Build()
            .GetSection(PostgreSqlIntegrationSettings.SectionName)
            .Get<PostgreSqlIntegrationSettings>() ?? new PostgreSqlIntegrationSettings();
}

/// <summary>The settings of the PostgreSQL of the integration tests. <c>appsettings.json</c> describes each value.</summary>
public sealed class PostgreSqlIntegrationSettings
{
    public const string SectionName = "PostgreSqlIntegration";

    public string? ConnectionString { get; set; }
}
