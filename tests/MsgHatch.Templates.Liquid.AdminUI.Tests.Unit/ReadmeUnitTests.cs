using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Templates.AdminUI.DependencyInjection;
using MsgHatch.Templates.Liquid.AdminUI.DependencyInjection;
using MsgHatch.Tests.Shared;

namespace MsgHatch.Templates.Liquid.AdminUI.Tests.Unit;

/// <summary>The examples of the README of the package run as tests, and the README check compares them (D311).</summary>
public sealed class ReadmeUnitTests
{
    [Fact]
    public void Readme_Snippets_EqualTheirSources() => ReadmeCheck.AssertSnippets();

    [Fact]
    public void Readme_OptionsTables_NameEachOption() => ReadmeCheck.AssertOptionsTables();

    [Fact]
    public void AddLiquidTemplateEditor_ReadmeSetup_AddsTheTimeProvider()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        #region readme:setup
        services.AddMsgHatchTemplatesAdminUI()
            .AddLiquidTemplateEditor();
        #endregion

        // Assert
        Assert.Contains(services, service => service.ServiceType == typeof(TimeProvider));
    }
}
