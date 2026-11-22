using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MsgHatch.Dispatch.NamedPipes.DependencyInjection;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using MsgHatch.Tests.Shared;

namespace MsgHatch.Dispatch.NamedPipes.Tests.Unit;

/// <summary>The examples of the README of the package run as tests, and the README check compares them (D311).</summary>
public sealed class ReadmeUnitTests
{
    [Fact]
    public void Readme_Snippets_EqualTheirSources() => ReadmeCheck.AssertSnippets();

    [Fact]
    public void Readme_OptionsTables_NameEachOption() => ReadmeCheck.AssertOptionsTables(typeof(NamedPipeSignalOptions));

    [Fact]
    public async Task UseNamedPipeDispatchSignal_SettingsOfTheReadme_BindsThePrefix()
    {
        // Arrange
        var services = TestHost.CreateServices(configuration: ReadmeCheck.ReadSettings("options.json"));
        services.AddMsgHatch().UseInMemoryStores();

        // Act
        #region readme:setup
        services.AddMsgHatch()
            .UseNamedPipeDispatchSignal();
        #endregion
        await using var provider = services.Build();

        // Assert
        Assert.Equal("shop", provider.GetRequiredService<IOptions<NamedPipeSignalOptions>>().Value.PipeNamePrefix);
    }

    [Fact]
    public async Task UseNamedPipeDispatchSignal_PrefixInCode_UsesTheValueOfTheCode()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatch().UseInMemoryStores();

        // Act
        #region readme:setup-code
        services.AddMsgHatch()
            .UseNamedPipeDispatchSignal(o => o.PipeNamePrefix = "shop");
        #endregion
        await using var provider = services.Build();

        // Assert
        Assert.Equal("shop", provider.GetRequiredService<IOptions<NamedPipeSignalOptions>>().Value.PipeNamePrefix);
    }
}
