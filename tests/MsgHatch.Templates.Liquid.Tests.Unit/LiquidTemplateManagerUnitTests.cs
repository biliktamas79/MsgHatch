using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Channels.Email.DependencyInjection;
using MsgHatch.Channels.Email.Tests.Unit.TestTypes;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Identity;
using MsgHatch.Templates.DependencyInjection;
using MsgHatch.Templates.Management;
using MsgHatch.Templates.Tests.Unit.TestTypes;

namespace MsgHatch.Templates.Liquid.Tests.Unit;

/// <summary>
/// The template manager with the Liquid engine of an email registration (D219, D222): a save checks the text with the
/// tree walk of the Liquid engine, and writes only a text that passes.
/// </summary>
public sealed class LiquidTemplateManagerUnitTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "msghatch-tests", Guid.NewGuid().ToString("N"));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("<p>{{ Name | upcse }}</p>", 422)]
    [InlineData("<p>{% render 'Footer' %}</p>", 422)]
    [InlineData("<p>{{ Name ", 422)]
    [InlineData("<p>{{ Name | upcase }}</p>", null)]
    public async Task SaveTemplateAsync_LiquidHtmlBody_GivesTheRuleOfTheCheckAndWritesOnlyAValidText(string html, int? expectedCode)
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatch(static o => o.DefaultLanguage = "en").UseFileTemplates(_root)
            .AddTestEvent(static m => m.WithPayload<OrderEmail, TestOrderLoader>(static p => p.AsEmail("OrderEmail", LiquidTemplates.Engine)));
        await using var provider = services.Build();
        await using var scope = provider.CreateAsyncScope();
        var lookup = new TemplateLookup(new MessageType("TestEvent"), "en", Scope: null, "body", TemplatePartKind.Html);

        // Act
        var result = await scope.ServiceProvider.GetRequiredService<ITemplateCommands>().SaveTemplateAsync(lookup, html, 0, Ct);

        // Assert
        Assert.Equal((expectedCode, expectedCode is null), (result.Error?.Type.Code, File.Exists(Path.Combine(_root, "TestEvent.en.body.html"))));
    }

    [Theory]
    [InlineData("a valid text", "<p>{{ Name | upcase }}</p>", 0, null, null)]
    [InlineData("a filter with no name", "<p>a</p>\n\n<p>{{ Name | }}</p>", 1, 3, 13)]
    [InlineData("an if with no endif", "<p>a</p>\n<p>{% if Name %}", 1, 2, 17)]
    [InlineData("an endif with no if", "ab\ncd {% endif %}", 1, 2, 12)]
    [InlineData("a liquid tag", "<p>{% liquid echo Name %}</p>", 1, null, null)]
    [InlineData("an unknown filter", "<p>{{ Name | upcse }}</p>", 1, null, null)]
    public async Task CheckTemplateAsync_LiquidHtmlBody_GivesTheLineAndTheColumnOfAParseErrorOfFluid(
        string row, string html, int expectedIssues, int? expectedLine, int? expectedColumn)
    {
        // Arrange
        await using var provider = ALiquidHost();
        await using var scope = provider.CreateAsyncScope();
        var lookup = new TemplateLookup(new MessageType("TestEvent"), "en", Scope: null, "body", TemplatePartKind.Html);

        // Act
        var issues = await scope.ServiceProvider.GetRequiredService<ITemplateQueries>().CheckTemplateAsync(lookup, html, Ct);

        // Assert
        var issue = issues.Value.Count > 0 ? issues.Value[0] : null;
        Assert.Equal((row, expectedIssues, expectedLine, expectedColumn), (row, issues.Value.Count, issue?.Line, issue?.Column));
    }

    [Fact]
    public async Task GetTemplatePartsAsync_EmailWithLiquid_GivesTheThreeEmailPartsWithTheEngineLiquid()
    {
        // Arrange
        await using var provider = ALiquidHost();
        await using var scope = provider.CreateAsyncScope();

        // Act
        var parts = await scope.ServiceProvider.GetRequiredService<ITemplateQueries>().GetTemplatePartsAsync(new MessageType("TestEvent"), Ct);

        // Assert
        Assert.Equal(
            ("liquid", "subject PlainText liquid; body Html liquid; body PlainText liquid"),
            (LiquidTemplates.Engine.Name, string.Join("; ", parts.Value.Select(static part => $"{part.Part.Name} {part.Part.Kind} {string.Join(",", part.Engines)}"))));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private ServiceProvider ALiquidHost()
    {
        var services = TestHost.CreateServices();
        services.AddMsgHatch(static o => o.DefaultLanguage = "en").UseFileTemplates(_root)
            .AddTestEvent(static m => m.WithPayload<OrderEmail, TestOrderLoader>(static p => p.AsEmail("OrderEmail", LiquidTemplates.Engine)));
        return services.Build();
    }
}
