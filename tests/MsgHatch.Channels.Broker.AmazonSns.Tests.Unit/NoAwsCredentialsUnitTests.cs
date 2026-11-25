using Amazon;
using Amazon.Runtime;
using Amazon.Runtime.Credentials;
using Amazon.SimpleNotificationService;

namespace MsgHatch.Channels.Broker.AmazonSns.Tests.Unit;

/// <summary>The SNS tests run with no AWS credentials, so that no test can send a request to AWS.</summary>
public sealed class NoAwsCredentialsUnitTests
{
    [Fact]
    public void GetCredentials_TestProcess_FindsNoAwsCredentials()
    {
        // Act
        var exception = Record.Exception(static () =>
            DefaultAWSCredentialsIdentityResolver.GetCredentials(new AmazonSimpleNotificationServiceConfig { RegionEndpoint = RegionEndpoint.EUWest1 }));

        // Assert
        Assert.IsType<AmazonClientException>(exception, exactMatch: false);
    }
}
