using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Email.DependencyInjection;
using MsgHatch.Email.Engines;
using MsgHatch.Email.Razor.DependencyInjection;
using MsgHatch.Email.Razor.Tests.Unit.TestTypes;
using MsgHatch.Email.Templates;
using MsgHatch.Email.Tests.Unit.Contracts;
using MsgHatch.Email.Tests.Unit.TestTypes;
using NSubstitute;

namespace MsgHatch.Email.Razor.Tests.Unit;

/// <summary>
/// The Razor engine: the use cases of the engine contract, and the rules that only this engine has: the company
/// templates (D195, D198, D201), one language for each email (D194), the parts of the store template (D197), and the
/// render (D202). The engine class is internal, so the test data builds it with <c>UseRazorEmailTemplates</c> and
/// <c>AsRazorEmail</c>.
/// </summary>
public sealed class RazorEmailEngineUnitTests(RazorTemplateTestData testData)
    : EmailTemplateEngineTestsBase<IEmailTemplateEngine<OrderEmail>>(testData.Engine, testData), IClassFixture<RazorTemplateTestData>
{
    private static readonly MessageType _company = new("Company");

    public static TheoryData<string, string, string?, string> CompanyRows => new()
    {
        { "a subject and a body of the company", "acme", "Acme subject", "Acme subject|<p>Acme Client 1</p>" },
        { "only a subject of the company", "globex", "Globex subject", "Globex subject|<p>Default Client 1</p>" },
        { "only a body of the company", "initech", null, "Default subject|<p>Initech Client 1</p>" },
        { "no template of the company", "umbrella", null, "Default subject|<p>Default Client 1</p>" },
        { "a numeric company id", "42", null, "Default subject|<p>Company 42 Client 1</p>" },
        { "a company id with a leading underscore", "_42", null, "Default subject|<p>Default Client 1</p>" },
        { "a company id with a dash", "acme-1", null, "Default subject|<p>Default Client 1</p>" },
    };

    public static TheoryData<string, string, string?, string> LanguageRows => new()
    {
        { "both parts in hu", "Language", "Language hu subject", "Language hu subject|<p>Language hu</p>" },
        { "no subject in hu", "NoHuSubject", null, "NoHuSubject en subject|<p>NoHuSubject en</p>" },
        { "no body in hu", "NoHuBody", "NoHuBody hu subject", "NoHuBody en subject|<p>NoHuBody en</p>" },
        { "each part in another language", "OnlyHuBody", null, "404" },
    };

    [Theory]
    [MemberData(nameof(CompanyRows))]
    public async Task PrepareAsync_RequestOfACompany_TakesEachPartFromTheCompanyOrElseFromTheDefault(
        string row, string company, string? companySubject, string expectedOutcome)
    {
        // Arrange
        testData.SeedSubject(_company, "en", scope: null, "Default subject");
        testData.SeedSubject(_company, "en", company, companySubject);

        // Act
        var outcome = await OutcomeOfAsync(new EmailTemplateRequest(_company, "en", company));

        // Assert
        Assert.Equal((row, expectedOutcome), (row, outcome));
    }

    [Theory]
    [MemberData(nameof(LanguageRows))]
    public async Task PrepareAsync_RequestInHungarian_TakesAllPartsFromOneLanguage(
        string row, string type, string? hungarianSubject, string expectedOutcome)
    {
        // Arrange
        testData.SeedSubject(new(type), "hu", scope: null, hungarianSubject);
        testData.SeedSubject(new(type), "en", scope: null, $"{type} en subject");

        // Act
        var outcome = await OutcomeOfAsync(new EmailTemplateRequest(new(type), "hu", Scope: null));

        // Assert
        Assert.Equal((row, expectedOutcome), (row, outcome));
    }

    [Fact]
    public async Task PrepareAsync_StoreTemplateWithAnHtmlPart_GivesTemplateInvalid()
    {
        // Arrange
        var type = new MessageType("WithHtmlPart");
        testData.SeedParts(
            new TemplateLookup(type, "en", Scope: null),
            [new TemplatePart("subject", TemplatePartKind.PlainText, "Order"), new TemplatePart("html", TemplatePartKind.Html, "<p>Order</p>")]);

        // Act
        var result = await Engine.PrepareAsync(new EmailTemplateRequest(type, "en", Scope: null), Ct);

        // Assert
        Assert.Equal(422, CodeOf(result));
    }

    [Fact]
    public async Task PrepareAsync_TemplateClassWithAnotherPayloadType_GivesTemplateInvalid()
    {
        // Arrange
        var type = new MessageType("OtherPayload");
        testData.SeedSubject(type, "en", scope: null, "Order");

        // Act
        var result = await Engine.PrepareAsync(new EmailTemplateRequest(type, "en", Scope: null), Ct);

        // Assert
        Assert.Equal(422, CodeOf(result));
    }

    [Fact]
    public async Task RenderAsync_CultureOfTheRoute_AppliesToTheComponentAndNotToTheCaller()
    {
        // Arrange
        var type = new MessageType("CallerCulture");
        testData.SeedSubject(type, "hu", scope: null, "Kultúra");
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");

        // Act
        var outcome = await OutcomeOfAsync(new EmailTemplateRequest(type, "hu", Scope: null));

        // Assert
        Assert.Equal(("Kultúra|<p>hu hu</p>", "en-US", "en-US"), (outcome, CultureInfo.CurrentCulture.Name, CultureInfo.CurrentUICulture.Name));
    }

    [Fact]
    public async Task RenderAsync_ComponentThatThrows_GivesTheExceptionToTheCaller()
    {
        // Arrange
        var type = new MessageType("Throws");
        testData.SeedSubject(type, "en", scope: null, "Order");

        // Act
        var exception = await Record.ExceptionAsync(() => OutcomeOfAsync(new EmailTemplateRequest(type, "en", Scope: null)));

        // Assert
        Assert.IsType<InvalidOperationException>(exception);
    }

    [Fact]
    public async Task RenderAsync_CancelledToken_ThrowsOperationCanceledException()
    {
        // Arrange
        var type = new MessageType("Cancelled");
        testData.SeedSubject(type, "en", scope: null, "Order");
        var template = await Engine.PrepareAsync(new EmailTemplateRequest(type, "en", Scope: null), Ct);

        // Act
        var exception = await Record.ExceptionAsync(async () =>
            await template.Value.RenderAsync(APayload(), TestRoutes.AnEmailRoute(), CultureInfo.InvariantCulture, new CancellationToken(canceled: true)));

        // Assert
        Assert.IsType<OperationCanceledException>(exception);
    }

    [Fact]
    public async Task PrepareAsync_EngineOfATenant_TakesTheSubjectFromTheTemplateStoreOfTheTenant()
    {
        // Arrange
        var tenantStore = AStoreWithTheSubject("Tenant subject");
        var services = TestHost.CreateServices();
        services.AddMsgHatch(static o => o.DefaultLanguage = "en")
            .UseTemplateStore(_ => AStoreWithTheSubject("Shared subject"))
            .UseTenantTemplateStore((_, _) => tenantStore)
            .UseRazorEmailTemplates(typeof(RazorTemplateTestData).Assembly, RazorTemplateTestData.Root)
            .AddTestEvent(static m => m.WithPayload<OrderEmail, TestOrderLoader>(static p => p.AsRazorEmail("OrderEmail")));
        await using var provider = services.Build();
        await using var scope = provider.CreateAsyncScope();
        var engine = scope.ServiceProvider.GetRequiredKeyedService<IEmailTemplateEngine<OrderEmail>>(TenantKey.For(new TenantId("t1")));

        // Act
        var outcome = await OutcomeOfAsync(engine, new EmailTemplateRequest(_company, "en", Scope: null));

        // Assert
        Assert.Equal("Tenant subject|<p>Default Client 1</p>", outcome);
    }

    private static ITemplateStore AStoreWithTheSubject(string subject)
    {
        var store = Substitute.For<ITemplateStore>();
        store.FindAsync(default, default).ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<TemplateSource?>.ForValue(
            new TemplateSource(new TemplateKey(Guid.NewGuid().ToString(), 0), [new TemplatePart("subject", TemplatePartKind.PlainText, subject)]))));
        return store;
    }

    private Task<string> OutcomeOfAsync(EmailTemplateRequest request) => OutcomeOfAsync(Engine, request);

    private static async Task<string> OutcomeOfAsync(IEmailTemplateEngine<OrderEmail> engine, EmailTemplateRequest request)
    {
        var template = await engine.PrepareAsync(request, Ct);
        if (template.IsError)
        {
            return template.Error.Type.Code.ToString(CultureInfo.InvariantCulture);
        }

        var rendered = await template.Value.RenderAsync(
            APayload(), TestRoutes.AnEmailRoute(language: request.Language), CultureInfo.GetCultureInfo(request.Language), Ct);
        return $"{rendered.Value.Subject}|{rendered.Value.HtmlBody}";
    }
}
