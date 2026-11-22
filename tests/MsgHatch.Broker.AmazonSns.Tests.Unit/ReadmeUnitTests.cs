using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MsgHatch.Broker.AmazonSns.DependencyInjection;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using MsgHatch.Tests.Shared;

namespace MsgHatch.Broker.AmazonSns.Tests.Unit;

/// <summary>
/// The examples of the README of the package run as tests, and the README check compares them (D311). The tests read
/// only the options, so the SNS client of the AWS SDK needs no credentials.
/// </summary>
public sealed class ReadmeUnitTests
{
    [Fact]
    public void Readme_Snippets_EqualTheirSources() => ReadmeCheck.AssertSnippets();

    [Fact]
    public void Readme_OptionsTables_NameEachOption() => ReadmeCheck.AssertOptionsTables(typeof(AmazonSnsOptions));

    [Fact]
    public async Task UseAmazonSnsBroker_SettingsOfTheReadme_PassTheValidation()
    {
        // Arrange
        var services = TestHost.CreateServices(configuration: ReadmeCheck.ReadSettings("options.json"));
        services.AddMsgHatch().UseInMemoryStores();

        // Act
        #region readme:setup
        services.AddMsgHatch()
            .UseAmazonSnsBroker();
        #endregion
        await using var provider = services.Build();

        // Assert
        var options = provider.GetRequiredService<IOptions<AmazonSnsOptions>>().Value;
        Assert.Equal("arn:aws:sns:eu-west-1:123456789012:shop-events", options.DefaultTopicArn);
        Assert.Equal("arn:aws:sns:eu-west-1:123456789012:shop-orders", options.Topics["OrderShipped"]);
    }

    [Fact]
    public async Task UseAmazonSnsBroker_RegionInCode_UsesTheValueOfTheCode()
    {
        // Arrange
        var services = TestHost.CreateServices(configuration: ReadmeCheck.ReadSettings("options.json"));
        services.AddMsgHatch().UseInMemoryStores();

        // Act
        #region readme:setup-code
        services.AddMsgHatch()
            .UseAmazonSnsBroker(o => o.Region = "eu-central-1");
        #endregion
        await using var provider = services.Build();

        // Assert
        Assert.Equal("eu-central-1", provider.GetRequiredService<IOptions<AmazonSnsOptions>>().Value.Region);
    }
}
