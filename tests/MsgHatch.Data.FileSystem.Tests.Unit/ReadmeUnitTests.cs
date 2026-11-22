using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MsgHatch.Data.FileSystem.DependencyInjection;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Outbox;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using MsgHatch.Tests.Shared;

namespace MsgHatch.Data.FileSystem.Tests.Unit;

/// <summary>The examples of the README of the package run as tests, and the README check compares them (D311).</summary>
public sealed class ReadmeUnitTests
{
    [Fact]
    public void Readme_Snippets_EqualTheirSources() => ReadmeCheck.AssertSnippets();

    [Fact]
    public void Readme_OptionsTables_NameEachOption() => ReadmeCheck.AssertOptionsTables(typeof(FileStoreOptions));

    [Fact]
    public async Task UseFileStores_SettingsOfTheReadme_ResolvesTheOutboxStore()
    {
        // Arrange
        var services = TestHost.CreateServices(configuration: ReadmeCheck.ReadSettings("options.json"));

        // Act
        #region readme:setup
        services.AddMsgHatch()
            .UseFileStores();
        #endregion
        await using var provider = services.Build();

        // Assert
        Assert.NotNull(provider.GetRequiredService<IOutboxStore>());
        Assert.Equal("data/msghatch", provider.GetRequiredService<IOptions<FileStoreOptions>>().Value.RootPath);
    }

    [Fact]
    public async Task UseFileStores_FolderInCode_UsesTheValueOfTheCode()
    {
        // Arrange
        var services = TestHost.CreateServices();

        // Act
        #region readme:setup-code
        services.AddMsgHatch()
            .UseFileStores("data/msghatch");
        #endregion
        await using var provider = services.Build();

        // Assert
        Assert.Equal("data/msghatch", provider.GetRequiredService<IOptions<FileStoreOptions>>().Value.RootPath);
    }
}
