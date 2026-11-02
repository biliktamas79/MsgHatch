using MsgHatch.Email.AmazonSES.Tests.Unit.TestTypes;

[assembly: AssemblyFixture(typeof(NoAwsCredentials))]

namespace MsgHatch.Email.AmazonSES.Tests.Unit.TestTypes;

/// <summary>
/// The assembly fixture that removes the AWS credentials and the AWS region of the developer from the test process
/// before the first test. A test that reaches a real SES client by mistake then fails with no credentials, and sends no
/// request to AWS. Each test that needs a region of the AWS SDK sets it in the process.
/// </summary>
public sealed class NoAwsCredentials
{
    private static readonly string _nowhere = Path.Combine(Path.GetTempPath(), "msghatch-no-aws", Guid.NewGuid().ToString("N"));

    private static readonly (string Name, string? Value)[] _environment =
    [
        ("AWS_ACCESS_KEY_ID", null),
        ("AWS_SECRET_ACCESS_KEY", null),
        ("AWS_SESSION_TOKEN", null),
        ("AWS_WEB_IDENTITY_TOKEN_FILE", null),
        ("AWS_ROLE_ARN", null),
        ("AWS_CONTAINER_CREDENTIALS_RELATIVE_URI", null),
        ("AWS_CONTAINER_CREDENTIALS_FULL_URI", null),
        ("AWS_CONTAINER_AUTHORIZATION_TOKEN", null),
        ("AWS_REGION", null),
        ("AWS_DEFAULT_REGION", null),
        ("AWS_PROFILE", "msghatch-tests-no-credentials"),
        ("AWS_SHARED_CREDENTIALS_FILE", Path.Combine(_nowhere, "credentials")),
        ("AWS_CONFIG_FILE", Path.Combine(_nowhere, "config")),
        ("AWS_EC2_METADATA_DISABLED", "true"),
    ];

    public NoAwsCredentials()
    {
        foreach (var (name, value) in _environment)
        {
            Environment.SetEnvironmentVariable(name, value);
        }
    }
}
