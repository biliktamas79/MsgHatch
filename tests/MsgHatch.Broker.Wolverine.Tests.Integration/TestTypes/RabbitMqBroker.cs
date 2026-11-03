using Microsoft.Extensions.Configuration;

namespace MsgHatch.Broker.Wolverine.Tests.Integration.TestTypes;

/// <summary>
/// The RabbitMQ broker of the integration tests, from the section <c>RabbitMqIntegration</c> of <c>appsettings.json</c>
/// and of <c>appsettings.Local.json</c>, which git ignores.
/// </summary>
public static class RabbitMqBroker
{
    public const string NotConfigured =
        "No RabbitMQ broker: set RabbitMqIntegration:Uri in appsettings.Local.json next to appsettings.json (git ignores it).";

    public static RabbitMqBrokerSettings Settings { get; } = Load();

    public static bool IsConfigured => !string.IsNullOrWhiteSpace(Settings.Uri);

    public static Uri Uri => new(Settings.Uri!);

    private static RabbitMqBrokerSettings Load() =>
        new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json")
            .AddJsonFile("appsettings.Local.json", optional: true)
            .Build()
            .GetSection(RabbitMqBrokerSettings.SectionName)
            .Get<RabbitMqBrokerSettings>() ?? new RabbitMqBrokerSettings();
}

/// <summary>The settings of the RabbitMQ broker of the integration tests. <c>appsettings.json</c> describes each value.</summary>
public sealed class RabbitMqBrokerSettings
{
    public const string SectionName = "RabbitMqIntegration";

    public string? Uri { get; set; }
}
