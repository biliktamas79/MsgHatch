using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MsgHatch.Channels.Email.Resend.DependencyInjection;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using MsgHatch.Tests.Shared;

namespace MsgHatch.Channels.Email.Resend.Tests.Unit;

/// <summary>
/// The examples of the README of the package run as tests, and the README check compares them (D311). The API key comes
/// from a second configuration key, as from a secret store.
/// </summary>
public sealed class ReadmeUnitTests
{
    [Fact]
    public void Readme_Snippets_EqualTheirSources() => ReadmeCheck.AssertSnippets();

    [Fact]
    public void Readme_OptionsTables_NameEachOption() => ReadmeCheck.AssertOptionsTables(typeof(ResendOptions));

    [Fact]
    public async Task UseResend_SettingsOfTheReadmeAndASecretKey_PassTheValidation()
    {
        // Arrange
        var services = TestHost.CreateServices(configuration: SettingsWithTheSecretKey());
        services.AddMsgHatch().UseInMemoryStores();

        // Act
        #region readme:setup
        services.AddMsgHatch()
            .UseResend();
        #endregion
        await using var provider = services.Build();

        // Assert
        var options = provider.GetRequiredService<IOptions<ResendOptions>>().Value;
        Assert.Equal(("re_test", "noreply@example.com", "Example Shop"), (options.ApiKey, options.SenderEmail, options.SenderName));
    }

    [Fact]
    public async Task UseResend_SenderNameInCode_UsesTheValueOfTheCode()
    {
        // Arrange
        var services = TestHost.CreateServices(configuration: SettingsWithTheSecretKey());
        services.AddMsgHatch().UseInMemoryStores();

        // Act
        #region readme:setup-code
        services.AddMsgHatch()
            .UseResend(o => o.SenderName = "Example Shop");
        #endregion
        await using var provider = services.Build();

        // Assert
        Assert.Equal("Example Shop", provider.GetRequiredService<IOptions<ResendOptions>>().Value.SenderName);
    }

    private static Dictionary<string, string?> SettingsWithTheSecretKey()
    {
        var settings = ReadmeCheck.ReadSettings("options.json");
        settings["MsgHatch:Resend:ApiKey"] = "re_test";
        return settings;
    }
}
