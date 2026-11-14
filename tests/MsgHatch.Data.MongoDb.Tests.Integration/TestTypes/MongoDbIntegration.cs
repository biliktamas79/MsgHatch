using Microsoft.Extensions.Configuration;

namespace MsgHatch.Data.MongoDb.Tests.Integration.TestTypes;

/// <summary>
/// The MongoDB of the MongoDB integration tests, from the section <c>MongoDbIntegration</c> of <c>appsettings.json</c>
/// and of <c>appsettings.Local.json</c>, which git ignores (D183).
/// </summary>
public static class MongoDbIntegration
{
    public static MongoDbIntegrationSettings Settings { get; } = Load();

    /// <summary>Whether a server is set. With no server, the tests start MongoDB with Testcontainers (D124).</summary>
    public static bool IsConfigured => !string.IsNullOrWhiteSpace(Settings.ConnectionString);

    private static MongoDbIntegrationSettings Load() =>
        new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json")
            .AddJsonFile("appsettings.Local.json", optional: true)
            .Build()
            .GetSection(MongoDbIntegrationSettings.SectionName)
            .Get<MongoDbIntegrationSettings>() ?? new MongoDbIntegrationSettings();
}

/// <summary>The settings of the MongoDB of the integration tests. <c>appsettings.json</c> describes each value.</summary>
public sealed class MongoDbIntegrationSettings
{
    public const string SectionName = "MongoDbIntegration";

    public string? ConnectionString { get; set; }
}
