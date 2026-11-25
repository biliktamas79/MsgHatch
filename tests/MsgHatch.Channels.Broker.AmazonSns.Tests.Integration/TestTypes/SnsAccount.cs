using Amazon;
using Amazon.Runtime;
using Amazon.SimpleNotificationService;
using Amazon.SQS;
using Microsoft.Extensions.Configuration;

namespace MsgHatch.Channels.Broker.AmazonSns.Tests.Integration.TestTypes;

/// <summary>
/// The AWS resources of the integration tests, from the section <c>SnsIntegration</c> of <c>appsettings.json</c> and of
/// <c>appsettings.Local.json</c>, which git ignores. The SNS and SQS clients use only these credentials, never the AWS
/// credentials of the machine.
/// </summary>
public static class SnsAccount
{
    public const string NotConfigured =
        "No SNS topic: set SnsIntegration in appsettings.Local.json next to appsettings.json (git ignores it).";

    public static SnsAccountSettings Settings { get; } = Load();

    public static bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Settings.AccessKeyId) && !string.IsNullOrWhiteSpace(Settings.SecretAccessKey)
        && !string.IsNullOrWhiteSpace(Settings.Region) && !string.IsNullOrWhiteSpace(Settings.TopicArn)
        && !string.IsNullOrWhiteSpace(Settings.QueueUrl);

    /// <summary>The ARN of a topic that does not exist, in the account and the region of the topic of the settings.</summary>
    public static string MissingTopicArn => $"{Settings.TopicArn![..(Settings.TopicArn!.LastIndexOf(':') + 1)]}msghatch-missing-{Guid.NewGuid():N}";

    public static IAmazonSimpleNotificationService NewSnsClient() =>
        new AmazonSimpleNotificationServiceClient(Credentials(), RegionEndpoint.GetBySystemName(Settings.Region));

    public static IAmazonSQS NewSqsClient() => new AmazonSQSClient(Credentials(), RegionEndpoint.GetBySystemName(Settings.Region));

    private static AWSCredentials Credentials() =>
        string.IsNullOrWhiteSpace(Settings.SessionToken)
            ? new BasicAWSCredentials(Settings.AccessKeyId, Settings.SecretAccessKey)
            : new SessionAWSCredentials(Settings.AccessKeyId, Settings.SecretAccessKey, Settings.SessionToken);

    private static SnsAccountSettings Load() =>
        new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json")
            .AddJsonFile("appsettings.Local.json", optional: true)
            .Build()
            .GetSection(SnsAccountSettings.SectionName)
            .Get<SnsAccountSettings>() ?? new SnsAccountSettings();
}

/// <summary>The settings of the AWS resources of the integration tests. <c>appsettings.json</c> describes each value.</summary>
public sealed class SnsAccountSettings
{
    public const string SectionName = "SnsIntegration";

    public string? AccessKeyId { get; set; }

    public string? SecretAccessKey { get; set; }

    public string? SessionToken { get; set; }

    public string? Region { get; set; }

    public string? TopicArn { get; set; }

    public string? QueueUrl { get; set; }
}
