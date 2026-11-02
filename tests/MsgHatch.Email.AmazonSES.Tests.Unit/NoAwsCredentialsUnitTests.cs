using Amazon;
using Amazon.Runtime;
using Amazon.Runtime.Credentials;
using Amazon.SimpleEmailV2;

namespace MsgHatch.Email.AmazonSES.Tests.Unit;

/// <summary>The SES tests run with no AWS credentials, so that no test can send a request to AWS.</summary>
public sealed class NoAwsCredentialsUnitTests
{
    [Fact]
    public void GetCredentials_TestProcess_FindsNoAwsCredentials()
    {
        // Act
        var exception = Record.Exception(static () =>
            DefaultAWSCredentialsIdentityResolver.GetCredentials(new AmazonSimpleEmailServiceV2Config { RegionEndpoint = RegionEndpoint.EUWest1 }));

        // Assert
        Assert.IsType<AmazonClientException>(exception, exactMatch: false);
    }
}
