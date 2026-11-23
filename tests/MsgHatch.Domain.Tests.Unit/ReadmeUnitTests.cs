using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Outbox;
using MsgHatch.Domain.Routing;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using MsgHatch.Tests.Shared;

namespace MsgHatch.Domain.Tests.Unit;

/// <summary>The examples of the README of the package run as tests, and the README check compares them (D311).</summary>
public sealed class ReadmeUnitTests
{
    [Fact]
    public void Readme_Snippets_EqualTheirSources() => ReadmeCheck.AssertSnippets();

    [Fact]
    public void Readme_OptionsTables_NameEachOption() =>
        ReadmeCheck.AssertOptionsTables(
            typeof(MsgHatchOptions), typeof(DispatchOptions), typeof(RetryOptions), typeof(ChannelDownOptions),
            typeof(TestDeliveryOptions), typeof(RegistryOptions));

    [Fact]
    public async Task AddMsgHatch_SettingsOfTheReadme_BindsEachOption()
    {
        // Arrange
        var services = TestHost.CreateServices(configuration: ReadmeCheck.ReadSettings("options.json"));

        // Act
        #region readme:setup
        services.AddMsgHatch(o => o.DefaultLanguage = "hu")
            .UseInMemoryStores();
        #endregion
        services.AddMsgHatch().UseStoredRegistry();
        await using var provider = services.Build();

        // Assert
        Assert.NotNull(provider.GetRequiredService<IOutboxStore>());
        Assert.Equal("hu", provider.GetRequiredService<IOptions<MsgHatchOptions>>().Value.DefaultLanguage);
        Assert.Equal(16, provider.GetRequiredService<IOptionsMonitor<DispatchOptions>>().Get("Outbox").MaxDegreeOfParallelism);
        Assert.Equal(10, provider.GetRequiredService<IOptionsMonitor<RetryOptions>>().Get("Inbox").MaxTryCount);
        Assert.Equal(TimeSpan.FromMinutes(1), provider.GetRequiredService<IOptions<RegistryOptions>>().Value.RefreshInterval);
    }
}
