using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Identity;
using MsgHatch.Email.DependencyInjection;
using MsgHatch.Email.Tests.Unit.TestTypes;
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
        var result = await scope.ServiceProvider.GetRequiredService<ITemplateManager>().SaveTemplateAsync(lookup, html, 0, Ct);

        // Assert
        Assert.Equal((expectedCode, expectedCode is null), (result.Error?.Type.Code, File.Exists(Path.Combine(_root, "TestEvent.en.body.html"))));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
