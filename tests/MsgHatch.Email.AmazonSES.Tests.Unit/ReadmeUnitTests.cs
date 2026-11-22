using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using MsgHatch.Email.AmazonSES.DependencyInjection;
using MsgHatch.Tests.Shared;

namespace MsgHatch.Email.AmazonSES.Tests.Unit;

/// <summary>
/// The examples of the README of the package run as tests, and the README check compares them (D311). The tests read
/// only the options, so the SES client of the AWS SDK needs no credentials.
/// </summary>
public sealed class ReadmeUnitTests
{
    [Fact]
    public void Readme_Snippets_EqualTheirSources() => ReadmeCheck.AssertSnippets();

    [Fact]
    public void Readme_OptionsTables_NameEachOption() => ReadmeCheck.AssertOptionsTables(typeof(AmazonSesOptions));

    [Fact]
    public async Task UseAmazonSes_SettingsOfTheReadme_PassTheValidation()
    {
        // Arrange
        var services = TestHost.CreateServices(configuration: ReadmeCheck.ReadSettings("options.json"));
        services.AddMsgHatch().UseInMemoryStores();

        // Act
        #region readme:setup
        services.AddMsgHatch()
            .UseAmazonSes();
        #endregion
        await using var provider = services.Build();

        // Assert
        var options = provider.GetRequiredService<IOptions<AmazonSesOptions>>().Value;
        Assert.Equal(("noreply@example.com", "Example Shop", "eu-west-1"), (options.SenderEmail, options.SenderName, options.Region));
    }

    [Fact]
    public async Task UseAmazonSes_SenderInCode_UsesTheValueOfTheCode()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatch().UseInMemoryStores();

        // Act
        #region readme:setup-code
        services.AddMsgHatch()
            .UseAmazonSes("noreply@example.com");
        #endregion
        await using var provider = services.Build();

        // Assert
        Assert.Equal("noreply@example.com", provider.GetRequiredService<IOptions<AmazonSesOptions>>().Value.SenderEmail);
    }
}
