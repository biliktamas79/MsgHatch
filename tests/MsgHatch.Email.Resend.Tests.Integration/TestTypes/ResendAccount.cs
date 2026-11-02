using Microsoft.Extensions.Configuration;

namespace MsgHatch.Email.Resend.Tests.Integration.TestTypes;

/// <summary>
/// The Resend account of the integration tests, from the section <c>ResendIntegration</c> of <c>appsettings.json</c> and
/// of <c>appsettings.Local.json</c>, which git ignores.
/// </summary>
public static class ResendAccount
{
    public const string NotConfigured =
        "No Resend account: set ResendIntegration:ApiKey in appsettings.Local.json next to appsettings.json (git ignores it).";

    public static ResendAccountSettings Settings { get; } = Load();

    public static bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Settings.ApiKey) && !string.IsNullOrWhiteSpace(Settings.SenderEmail)
        && !string.IsNullOrWhiteSpace(Settings.Recipient);

    private static ResendAccountSettings Load() =>
        new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json")
            .AddJsonFile("appsettings.Local.json", optional: true)
            .Build()
            .GetSection(ResendAccountSettings.SectionName)
            .Get<ResendAccountSettings>() ?? new ResendAccountSettings();
}

/// <summary>The settings of the Resend account of the integration tests. <c>appsettings.json</c> describes each value.</summary>
public sealed class ResendAccountSettings
{
    public const string SectionName = "ResendIntegration";

    public string? ApiKey { get; set; }

    public string? SenderEmail { get; set; }

    public string? Recipient { get; set; }
}
