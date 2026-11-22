using Amazon;
using Amazon.Runtime;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MsgHatch.Broker.AmazonSns.DependencyInjection;
using MsgHatch.Broker.Tests.Unit.TestTypes;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using NSubstitute;

namespace MsgHatch.Broker.AmazonSns.Tests.Unit.DependencyInjection;

/// <summary>
/// <c>UseAmazonSnsBroker</c> alone registers all parts of the package (section 4.7, D60, D300). A test that resolves the
/// real SNS client sends no request, so it needs no AWS credentials. The AWS SDK keeps the region of its configuration
/// after the first use, so each test that resolves a client with no region sets that region and clears the kept value,
/// and the class sets both back at the end.
/// </summary>
public sealed class AmazonSnsBrokerExtensionsUnitTests : IDisposable
{
    private const string _topic = "arn:aws:sns:eu-west-1:123456789012:orders";
    private const string _otherTopic = "arn:aws:sns:eu-west-1:123456789012:other";

    private readonly string? _awsRegion = AWSConfigs.AWSRegion;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => UseTheRegionOfTheAwsSdk(_awsRegion);

    [Fact]
    public async Task UseAmazonSnsBroker_Alone_ResolvesTheBrokerClientAsTheLoggingWrapperAroundTheSnsClient()
    {
        // Arrange
        var sns = AnAcceptingSns();
        var services = TestHost.CreateServices();
        services.AddSingleton(sns);
        services.AddMsgHatch().UseAmazonSnsBroker(static o => o.DefaultTopicArn = _topic);
        await using var provider = services.Build();
        var client = provider.GetRequiredService<IBrokerClient>();

        // Act
        await client.PublishAsync(APublish(), Ct);

        // Assert
        Assert.IsNotType<AmazonSnsBrokerClient>(client);
        Assert.Equal(2300, Assert.Single(provider.LogRecords()).Id.Id);
        await sns.ReceivedWithAnyArgs(1).PublishAsync(default(PublishRequest)!, Ct);
    }

    [Fact]
    public async Task UseAmazonSnsBroker_Alone_RegistersTheTransportOfTheBrokerChannel()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddSingleton(AnAcceptingSns());
        services.AddMsgHatch().UseAmazonSnsBroker(static o => o.DefaultTopicArn = _topic);
        await using var provider = services.Build();

        // Act
        var transport = Assert.Single(provider.GetServices<ITransport>());

        // Assert
        Assert.Equal(BrokerChannel.Id, transport.Channel);
    }

    [Fact]
    public async Task UseAmazonSnsBroker_Region_RegistersTheSnsClientOfThatRegion()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatch().UseAmazonSnsBroker(static o => { o.DefaultTopicArn = _topic; o.Region = "eu-west-1"; });
        await using var provider = services.Build();

        // Act
        var sns = provider.GetRequiredService<IAmazonSimpleNotificationService>();

        // Assert
        Assert.Equal(RegionEndpoint.EUWest1, Assert.IsType<AmazonSimpleNotificationServiceClient>(sns).Config.RegionEndpoint);
    }

    [Fact]
    public async Task UseAmazonSnsBroker_NoRegion_RegistersTheSnsClientOfTheRegionOfTheAwsSdkConfiguration()
    {
        // Arrange
        UseTheRegionOfTheAwsSdk("eu-central-1");
        var services = TestHost.CreateServices();
        services.AddMsgHatch().UseAmazonSnsBroker(static o => o.DefaultTopicArn = _topic);
        await using var provider = services.Build();

        // Act
        var sns = provider.GetRequiredService<IAmazonSimpleNotificationService>();

        // Assert
        Assert.Equal(RegionEndpoint.EUCentral1, Assert.IsType<AmazonSimpleNotificationServiceClient>(sns).Config.RegionEndpoint);
    }

    [Fact]
    public async Task Validate_NoRegionInTheOptionsAndInTheAwsSdk_ThrowsOptionsValidationExceptionForTheRegion()
    {
        // Arrange
        UseTheRegionOfTheAwsSdk(region: null);
        var services = TestHost.CreateServices();
        services.AddMsgHatch().UseInMemoryStores().UseAmazonSnsBroker(static o => o.DefaultTopicArn = _topic);
        await using var provider = services.Build();

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        var validation = Assert.IsType<OptionsValidationException>(exception);
        Assert.Equal((typeof(AmazonSnsOptions), nameof(AmazonSnsOptions.Region)), (validation.OptionsType, Assert.Single(FailedProperties(validation))));
    }

    [Fact]
    public async Task UseAmazonSnsBroker_SnsClientThatTheHostRegisteredFirst_KeepsThatClient()
    {
        // Arrange
        var sns = Substitute.For<IAmazonSimpleNotificationService>();
        var services = TestHost.CreateServices();
        services.AddSingleton(sns);
        services.AddMsgHatch().UseAmazonSnsBroker(static o => o.DefaultTopicArn = _topic);
        await using var provider = services.Build();

        // Act
        var resolved = provider.GetRequiredService<IAmazonSimpleNotificationService>();

        // Assert
        Assert.Same(sns, resolved);
    }

    [Fact]
    public async Task UseAmazonSnsBroker_Section_BindsTheRegionTheDefaultTopicAndTheTopicOfEachRegistration()
    {
        // Arrange
        var services = TestHost.CreateServices(configuration: new Dictionary<string, string?>
        {
            ["MsgHatch:AmazonSns:Region"] = "eu-west-1",
            ["MsgHatch:AmazonSns:DefaultTopicArn"] = _otherTopic,
            ["MsgHatch:AmazonSns:Topics:TestMessage"] = _topic,
        });
        services.AddSingleton(AnAcceptingSns());
        services.AddMsgHatch().UseAmazonSnsBroker();
        await using var provider = services.Build();

        // Act
        var options = provider.GetRequiredService<IOptions<AmazonSnsOptions>>().Value;

        // Assert
        Assert.Equal(("eu-west-1", _otherTopic), (options.Region, options.DefaultTopicArn));
        Assert.Equal([new KeyValuePair<string, string>("TestMessage", _topic)], options.Topics);
    }

    [Fact]
    public async Task UseAmazonSnsBroker_TopicOfTheConfiguration_PublishesTheMessageOfThatRegistrationToThatTopic()
    {
        // Arrange
        var sns = AnAcceptingSns();
        var services = TestHost.CreateServices(configuration: new Dictionary<string, string?>
        {
            ["MsgHatch:AmazonSns:Topics:TestMessage"] = _topic,
        });
        services.AddSingleton(sns);
        services.AddMsgHatch().UseAmazonSnsBroker();
        await using var provider = services.Build();

        // Act
        await provider.GetRequiredService<IBrokerClient>().PublishAsync(APublish(), Ct);

        // Assert
        await sns.Received(1).PublishAsync(Arg.Is<PublishRequest>(static r => r.TopicArn == _topic), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UseAmazonSnsBroker_TopicInCodeAndInTheConfiguration_PublishesToTheTopicOfTheCode()
    {
        // Arrange
        var sns = AnAcceptingSns();
        var services = TestHost.CreateServices(configuration: new Dictionary<string, string?>
        {
            ["MsgHatch:AmazonSns:Topics:TestMessage"] = _otherTopic,
        });
        services.AddSingleton(sns);
        services.AddMsgHatch().UseAmazonSnsBroker(static o => o.Topics["TestMessage"] = _topic);
        await using var provider = services.Build();

        // Act
        await provider.GetRequiredService<IBrokerClient>().PublishAsync(APublish(), Ct);

        // Assert
        await sns.Received(1).PublishAsync(Arg.Is<PublishRequest>(static r => r.TopicArn == _topic), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Validate_NoTopic_ThrowsOptionsValidationExceptionForTheTopics()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddSingleton(Substitute.For<IAmazonSimpleNotificationService>());
        services.AddMsgHatch().UseInMemoryStores().UseAmazonSnsBroker();
        await using var provider = services.Build();

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        var validation = Assert.IsType<OptionsValidationException>(exception);
        Assert.Equal((typeof(AmazonSnsOptions), nameof(AmazonSnsOptions.Topics)), (validation.OptionsType, Assert.Single(FailedProperties(validation))));
    }

    private static string[] FailedProperties(OptionsValidationException validation) =>
        [.. validation.Failures.Select(static failure => failure[..failure.IndexOf(':', StringComparison.Ordinal)])];

    private static BrokerPublish APublish() =>
        new(
            Guid.NewGuid(), Tenant: null, BrokerMessages.ContentType, new TestPayload("text"), BrokerTestJsonContext.Default.TestPayload,
            new Dictionary<string, string> { ["msghatch-id"] = "id-1" });

    private static void UseTheRegionOfTheAwsSdk(string? region)
    {
        AWSConfigs.AWSRegion = region;
        FallbackRegionFactory.Reset();
    }

    private static IAmazonSimpleNotificationService AnAcceptingSns()
    {
        var sns = Substitute.For<IAmazonSimpleNotificationService>();
        sns.PublishAsync(default(PublishRequest)!, Ct).ReturnsForAnyArgs(Task.FromResult(new PublishResponse { MessageId = "sns-1" }));
        return sns;
    }
}
