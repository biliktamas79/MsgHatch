using Microsoft.Extensions.Configuration;

namespace MsgHatch.Data.EFCore.Tests.Integration.TestTypes;

/// <summary>
/// The MySQL of the EF Core integration tests, from the section <c>MySqlIntegration</c> of <c>appsettings.json</c> and of
/// <c>appsettings.Local.json</c>, which git ignores (D180).
/// </summary>
public static class MySqlIntegration
{
    public static MySqlIntegrationSettings Settings { get; } = Load();

    /// <summary>Whether a server is set. With no server, the tests start MySQL with Testcontainers (D124).</summary>
    public static bool IsConfigured => !string.IsNullOrWhiteSpace(Settings.ConnectionString);

    private static MySqlIntegrationSettings Load() =>
        new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json")
            .AddJsonFile("appsettings.Local.json", optional: true)
            .Build()
            .GetSection(MySqlIntegrationSettings.SectionName)
            .Get<MySqlIntegrationSettings>() ?? new MySqlIntegrationSettings();
}

/// <summary>The settings of the MySQL of the integration tests. <c>appsettings.json</c> describes each value.</summary>
public sealed class MySqlIntegrationSettings
{
    public const string SectionName = "MySqlIntegration";

    public string? ConnectionString { get; set; }
}
