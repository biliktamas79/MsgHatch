using Amazon;
using Amazon.Runtime;
using Amazon.SimpleEmailV2;
using Amazon.SimpleEmailV2.Model;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Email.AmazonSES.DependencyInjection;
using MsgHatch.Email.Tests.Unit.TestTypes;
using MsgHatch.Domain.Failures;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using SesConflictClassifier = MsgHatch.Domain.Tests.Unit.TestTypes.ConflictClassifier<MsgHatch.Email.AmazonSES.AmazonSimpleEmailServiceClient>;

namespace MsgHatch.Email.AmazonSES.Tests.Unit.DependencyInjection;

/// <summary>
/// <c>UseAmazonSes</c> alone registers all parts of the package (section 4.7, D60). A test that resolves the real SES
/// client sends no request, so it needs no AWS credentials. The AWS SDK keeps the region of its configuration after the
/// first use, so each test that resolves a client with no region sets that region and clears the kept value, and the
/// class sets both back at the end.
/// </summary>
public sealed class AmazonSesExtensionsUnitTests : IDisposable
{
    private const string _senderEmail = "noreply@example.com";

    private readonly string? _awsRegion = AWSConfigs.AWSRegion;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => UseTheRegionOfTheAwsSdk(_awsRegion);

    [Fact]
    public async Task UseAmazonSes_Alone_ResolvesTheEmailClientAsTheLoggingWrapperAroundTheSesClient()
    {
        // Arrange
        var ses = AnAcceptingSes();
        var services = TestHost.CreateServices();
        services.AddSingleton(ses);
        services.AddMsgHatch().UseAmazonSes(static o => o.SenderEmail = _senderEmail);
        await using var provider = services.Build();
        var client = provider.GetRequiredService<IEmailClient>();

        // Act
        await client.SendAsync(Guid.NewGuid(), TestMessages.AnEmail(), Ct);

        // Assert
        Assert.IsNotType<AmazonSimpleEmailServiceClient>(client);
        Assert.Equal(2004, Assert.Single(provider.LogRecords()).Id.Id);
        await ses.ReceivedWithAnyArgs(1).SendEmailAsync(default!, Ct);
    }

    [Fact]
    public async Task UseAmazonSes_ExceptionClassifierOfTheClient_GivesItsErrorType()
    {
        // Arrange
        var ses = Substitute.For<IAmazonSimpleEmailServiceV2>();
        ses.SendEmailAsync(default!, Ct).ThrowsAsyncForAnyArgs(new HttpRequestException());
        var services = TestHost.CreateServices();
        services.AddSingleton(ses);
        services.AddMsgHatch().UseAmazonSes(static o => o.SenderEmail = _senderEmail)
            .AddExceptionClassifier<AmazonSimpleEmailServiceClient, SesConflictClassifier>();
        await using var provider = services.Build();

        // Act
        var result = await provider.GetRequiredService<IEmailClient>().SendAsync(Guid.NewGuid(), TestMessages.AnEmail(), Ct);

        // Assert
        Assert.Equal(Errors.Conflict, result.Error?.Type);
    }

    [Fact]
    public async Task UseAmazonSes_Alone_RegistersTheTransportOfTheEmailChannel()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddSingleton(AnAcceptingSes());
        services.AddMsgHatch().UseAmazonSes(static o => o.SenderEmail = _senderEmail);
        await using var provider = services.Build();

        // Act
        var transport = Assert.Single(provider.GetServices<ITransport>());

        // Assert
        Assert.Equal(EmailChannel.Id, transport.Channel);
    }

    [Fact]
    public async Task UseAmazonSes_Region_RegistersTheSesClientOfThatRegion()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatch().UseAmazonSes(static o => { o.SenderEmail = _senderEmail; o.Region = "eu-west-1"; });
        await using var provider = services.Build();

        // Act
        var ses = provider.GetRequiredService<IAmazonSimpleEmailServiceV2>();

        // Assert
        Assert.Equal(RegionEndpoint.EUWest1, Assert.IsType<AmazonSimpleEmailServiceV2Client>(ses).Config.RegionEndpoint);
    }

    [Fact]
    public async Task UseAmazonSes_NoRegion_RegistersTheSesClientOfTheRegionOfTheAwsSdkConfiguration()
    {
        // Arrange
        UseTheRegionOfTheAwsSdk("eu-central-1");
        var services = TestHost.CreateServices();
        services.AddMsgHatch().UseAmazonSes(static o => o.SenderEmail = _senderEmail);
        await using var provider = services.Build();

        // Act
        var ses = provider.GetRequiredService<IAmazonSimpleEmailServiceV2>();

        // Assert
        Assert.Equal(RegionEndpoint.EUCentral1, Assert.IsType<AmazonSimpleEmailServiceV2Client>(ses).Config.RegionEndpoint);
    }

    [Fact]
    public async Task Validate_NoRegionInTheOptionsAndInTheAwsSdk_ThrowsOptionsValidationExceptionForTheRegion()
    {
        // Arrange
        UseTheRegionOfTheAwsSdk(region: null);
        var services = TestHost.CreateServices();
        services.AddMsgHatch().UseInMemoryStores().UseAmazonSes(static o => o.SenderEmail = _senderEmail);
        await using var provider = services.Build();

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        var validation = Assert.IsType<OptionsValidationException>(exception);
        var failure = Assert.Single(validation.Failures);
        Assert.Equal((typeof(AmazonSesOptions), "Region"), (validation.OptionsType, failure[..failure.IndexOf(':', StringComparison.Ordinal)]));
    }

    [Fact]
    public async Task UseAmazonSes_SesClientThatTheHostRegisteredFirst_KeepsThatClient()
    {
        // Arrange
        var ses = Substitute.For<IAmazonSimpleEmailServiceV2>();
        var services = TestHost.CreateServices();
        services.AddSingleton(ses);
        services.AddMsgHatch().UseAmazonSes(static o => o.SenderEmail = _senderEmail);
        await using var provider = services.Build();

        // Act
        var resolved = provider.GetRequiredService<IAmazonSimpleEmailServiceV2>();

        // Assert
        Assert.Same(ses, resolved);
    }

    [Fact]
    public async Task UseAmazonSes_SenderEmailInTheConfiguration_SendsFromThatAddress()
    {
        // Arrange
        var ses = AnAcceptingSes();
        var services = TestHost.CreateServices(configuration: new Dictionary<string, string?> { ["MsgHatch:AmazonSes:SenderEmail"] = "config@example.com" });
        services.AddSingleton(ses);
        services.AddMsgHatch().UseAmazonSes();
        await using var provider = services.Build();

        // Act
        await provider.GetRequiredService<IEmailClient>().SendAsync(Guid.NewGuid(), TestMessages.AnEmail(), Ct);

        // Assert
        await ses.Received(1).SendEmailAsync(Arg.Is<SendEmailRequest>(static r => r.FromEmailAddress == "config@example.com"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UseAmazonSes_SenderEmailInCodeAndInTheConfiguration_SendsFromTheAddressOfTheCode()
    {
        // Arrange
        var ses = AnAcceptingSes();
        var services = TestHost.CreateServices(configuration: new Dictionary<string, string?> { ["MsgHatch:AmazonSes:SenderEmail"] = "config@example.com" });
        services.AddSingleton(ses);
        services.AddMsgHatch().UseAmazonSes(static o => o.SenderEmail = _senderEmail);
        await using var provider = services.Build();

        // Act
        await provider.GetRequiredService<IEmailClient>().SendAsync(Guid.NewGuid(), TestMessages.AnEmail(), Ct);

        // Assert
        await ses.Received(1).SendEmailAsync(Arg.Is<SendEmailRequest>(static r => r.FromEmailAddress == _senderEmail), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UseAmazonSes_SenderEmailString_SendsFromThatAddressAndWinsOverTheConfiguration()
    {
        // Arrange
        var ses = AnAcceptingSes();
        var services = TestHost.CreateServices(configuration: new Dictionary<string, string?> { ["MsgHatch:AmazonSes:SenderEmail"] = "config@example.com" });
        services.AddSingleton(ses);
        services.AddMsgHatch().UseAmazonSes("code@example.com");
        await using var provider = services.Build();

        // Act
        await provider.GetRequiredService<IEmailClient>().SendAsync(Guid.NewGuid(), TestMessages.AnEmail(), Ct);

        // Assert
        await ses.Received(1).SendEmailAsync(Arg.Is<SendEmailRequest>(static r => r.FromEmailAddress == "code@example.com"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Validate_NoSenderEmail_ThrowsOptionsValidationException()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddSingleton(Substitute.For<IAmazonSimpleEmailServiceV2>());
        services.AddMsgHatch().UseInMemoryStores().UseAmazonSes();
        await using var provider = services.Build();

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        Assert.IsType<OptionsValidationException>(exception);
    }

    private static void UseTheRegionOfTheAwsSdk(string? region)
    {
        AWSConfigs.AWSRegion = region;
        FallbackRegionFactory.Reset();
    }

    private static IAmazonSimpleEmailServiceV2 AnAcceptingSes()
    {
        var ses = Substitute.For<IAmazonSimpleEmailServiceV2>();
        ses.SendEmailAsync(default!, Ct).ReturnsForAnyArgs(Task.FromResult(new SendEmailResponse { MessageId = "ses-1" }));
        return ses;
    }
}
