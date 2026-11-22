using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Subscriptions.AdminUI.DependencyInjection;
using MsgHatch.Tests.Shared;

namespace MsgHatch.Subscriptions.AdminUI.Tests.Unit;

/// <summary>The examples of the README of the package run as tests, and the README check compares them (D311).</summary>
public sealed class ReadmeUnitTests
{
    [Fact]
    public void Readme_Snippets_EqualTheirSources() => ReadmeCheck.AssertSnippets();

    [Fact]
    public void Readme_OptionsTables_NameEachOption() => ReadmeCheck.AssertOptionsTables();

    [Fact]
    public void AddMsgHatchSubscriptionsAdminUI_ReadmeSetup_AddsServices()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        #region readme:setup
        services.AddMsgHatchSubscriptionsAdminUI();
        #endregion

        // Assert
        Assert.NotEmpty(services);
    }
}
