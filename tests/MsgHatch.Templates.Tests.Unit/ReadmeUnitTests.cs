using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using MsgHatch.Templates.DependencyInjection;
using MsgHatch.Templates.Placeholders;
using MsgHatch.Tests.Shared;

namespace MsgHatch.Templates.Tests.Unit;

/// <summary>The examples of the README of the package run as tests, and the README check compares them (D311).</summary>
public sealed class ReadmeUnitTests
{
    [Fact]
    public void Readme_Snippets_EqualTheirSources() => ReadmeCheck.AssertSnippets();

    [Fact]
    public void Readme_OptionsTables_NameEachOption() => ReadmeCheck.AssertOptionsTables(typeof(FileTemplateOptions));

    [Fact]
    public async Task UseFileTemplates_SettingsOfTheReadme_ResolvesTheTemplateStore()
    {
        // Arrange
        var services = TestHost.CreateServices(configuration: ReadmeCheck.ReadSettings("options.json"));
        services.AddMsgHatch().UseInMemoryStores();

        // Act
        #region readme:setup
        services.AddMsgHatch()
            .UseFileTemplates();
        #endregion
        await using var provider = services.Build();

        // Assert
        Assert.NotNull(provider.GetRequiredService<ITemplateStore>());
        Assert.Equal("Templates", provider.GetRequiredService<IOptions<FileTemplateOptions>>().Value.RootPath);
    }

    [Fact]
    public async Task UseFileTemplates_FolderInCode_UsesTheValueOfTheCode()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatch().UseInMemoryStores();

        // Act
        #region readme:setup-code
        services.AddMsgHatch()
            .UseFileTemplates("Templates");
        #endregion
        await using var provider = services.Build();

        // Assert
        Assert.Equal("Templates", provider.GetRequiredService<IOptions<FileTemplateOptions>>().Value.RootPath);
    }

    [Fact]
    public void PlaceholderPayload_PayloadOfTheReadme_GetsAPlaceholderWriter()
    {
        // Act
        var index = IndexOf<OrderShippedEmail>("OrderNumber");

        // Assert
        Assert.Equal(0, index);
    }

    private static int IndexOf<TPayload>(string propertyName)
        where TPayload : IPlaceholderPayload<TPayload> => TPayload.Writer.IndexOf(propertyName);
}

#region readme:payload
[PlaceholderPayload]
public sealed partial record OrderShippedEmail(string OrderNumber, decimal Total);
#endregion
