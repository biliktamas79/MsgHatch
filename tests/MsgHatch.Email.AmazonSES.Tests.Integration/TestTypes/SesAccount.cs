using Amazon;
using Amazon.Runtime;
using Amazon.SimpleEmailV2;
using Microsoft.Extensions.Configuration;

namespace MsgHatch.Email.AmazonSES.Tests.Integration.TestTypes;

/// <summary>
/// The SES account of the integration tests, from the section <c>SesIntegration</c> of <c>appsettings.json</c> and of
/// <c>appsettings.Local.json</c>, which git ignores. The SES client uses only these credentials, never the AWS
/// credentials of the machine.
/// </summary>
public static class SesAccount
{
    public const string NotConfigured =
        "No SES account: set SesIntegration in appsettings.Local.json next to appsettings.json (git ignores it).";

    public static SesAccountSettings Settings { get; } = Load();

    public static bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Settings.AccessKeyId) && !string.IsNullOrWhiteSpace(Settings.SecretAccessKey)
        && !string.IsNullOrWhiteSpace(Settings.Region) && !string.IsNullOrWhiteSpace(Settings.SenderEmail)
        && !string.IsNullOrWhiteSpace(Settings.Recipient);

    /// <summary>The domain of the sender, for an address that SES must refuse.</summary>
    public static string SenderDomain => Settings.SenderEmail![(Settings.SenderEmail!.LastIndexOf('@') + 1)..].TrimEnd('>');

    public static IAmazonSimpleEmailServiceV2 NewSesClient() =>
        string.IsNullOrWhiteSpace(Settings.ServiceUrl)
            ? new AmazonSimpleEmailServiceV2Client(Credentials(), RegionEndpoint.GetBySystemName(Settings.Region))
            : new AmazonSimpleEmailServiceV2Client(
                Credentials(), new AmazonSimpleEmailServiceV2Config { ServiceURL = Settings.ServiceUrl, AuthenticationRegion = Settings.Region });

    private static AWSCredentials Credentials() =>
        string.IsNullOrWhiteSpace(Settings.SessionToken)
            ? new BasicAWSCredentials(Settings.AccessKeyId, Settings.SecretAccessKey)
            : new SessionAWSCredentials(Settings.AccessKeyId, Settings.SecretAccessKey, Settings.SessionToken);

    private static SesAccountSettings Load() =>
        new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json")
            .AddJsonFile("appsettings.Local.json", optional: true)
            .Build()
            .GetSection(SesAccountSettings.SectionName)
            .Get<SesAccountSettings>() ?? new SesAccountSettings();
}

/// <summary>The settings of the SES account of the integration tests. <c>appsettings.json</c> describes each value.</summary>
public sealed class SesAccountSettings
{
    public const string SectionName = "SesIntegration";

    public string? AccessKeyId { get; set; }

    public string? SecretAccessKey { get; set; }

    public string? SessionToken { get; set; }

    public string? Region { get; set; }

    public string? SenderEmail { get; set; }

    public string? Recipient { get; set; }

    public string? ServiceUrl { get; set; }
}
