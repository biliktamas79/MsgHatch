using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using MsgHatch.Email.DependencyInjection;
using MsgHatch.Email.Tests.Unit.TestTypes;
using OrderEmail = MsgHatch.Templates.Tests.Unit.TestTypes.OrderEmail;
using MsgHatch.Tests.Shared;
using OrderEmailLoader = MsgHatch.Email.Tests.Unit.TestTypes.TestOrderLoader;

namespace MsgHatch.Templates.Liquid.Tests.Unit;

/// <summary>The examples of the README of the package run as tests, and the README check compares them (D311).</summary>
public sealed class ReadmeUnitTests
{
    [Fact]
    public void Readme_Snippets_EqualTheirSources() => ReadmeCheck.AssertSnippets();

    [Fact]
    public void Readme_OptionsTables_NameEachOption() => ReadmeCheck.AssertOptionsTables();

    [Fact]
    public async Task AsEmail_LiquidEngineOfTheReadme_BuildsTheServices()
    {
        // Arrange
        var services = TestHost.CreateServices();

        // Act
        services.AddMsgHatch().UseInMemoryStores().UseTestTemplates()
            .AddTestEvent(m => m
                #region readme:email
                .WithPayload<OrderEmail, OrderEmailLoader>(p => p.AsEmail("OrderEmail", LiquidTemplates.Engine))
                #endregion
            );
        await using var provider = services.Build();

        // Assert
        Assert.NotNull(provider);
    }
}
