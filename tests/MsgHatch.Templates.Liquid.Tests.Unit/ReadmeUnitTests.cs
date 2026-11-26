using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Channels.Email.DependencyInjection;
using MsgHatch.Channels.Email.Tests.Unit.TestTypes;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using OrderEmail = MsgHatch.Templates.Tests.Unit.TestTypes.OrderEmail;
using MsgHatch.Templates.Management;
using MsgHatch.Tests.Shared;
using OrderEmailLoader = MsgHatch.Channels.Email.Tests.Unit.TestTypes.TestOrderLoader;

namespace MsgHatch.Templates.Liquid.Tests.Unit;

/// <summary>The examples of the README of the package run as tests, and the README check compares them (D311).</summary>
public sealed class ReadmeUnitTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

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
        await using var scope = provider.CreateAsyncScope();
        var parts = await scope.ServiceProvider.GetRequiredService<ITemplateQueries>().GetTemplatePartsAsync(new MessageType("TestEvent"), Ct);
        Assert.Equal(
            ["subject PlainText liquid", "body Html liquid", "body PlainText liquid"],
            parts.Value.Select(static part => $"{part.Part.Name} {part.Part.Kind} {string.Join(",", part.Engines)}"));
    }
}
