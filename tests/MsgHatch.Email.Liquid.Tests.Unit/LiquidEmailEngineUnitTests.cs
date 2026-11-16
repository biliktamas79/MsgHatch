using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Email.DependencyInjection;
using MsgHatch.Email.Engines;
using MsgHatch.Email.Liquid.DependencyInjection;
using MsgHatch.Email.Liquid.Tests.Unit.TestTypes;
using MsgHatch.Email.Templates;
using MsgHatch.Email.Tests.Unit.Contracts;
using MsgHatch.Email.Tests.Unit.TestTypes;
using NSubstitute;

namespace MsgHatch.Email.Liquid.Tests.Unit;

/// <summary>
/// The Liquid engine: the use cases of the engine contract, and the rules that only this engine has: the parse at
/// prepare, the unknown names (D207), the partials (D208), the model (D209), the render (D210), the cache of the parsed
/// templates, and the lookups of D123 and D199. The engine class is internal, so the test data builds it with
/// <c>AsLiquidEmail</c>.
/// </summary>
public sealed class LiquidEmailEngineUnitTests(LiquidTemplateTestData testData)
    : EmailTemplateEngineTestsBase<IEmailTemplateEngine<OrderEmail>>(testData.Engine, testData), IClassFixture<LiquidTemplateTestData>
{
    public static TheoryData<string, string?, string?, string?, string> PrepareRows => new()
    {
        { "a parse error in the subject", "Hello {{ Name ", "<p>html</p>", null, "prepare 422" },
        { "a parse error in the html", "Order", "<p>{% if Note %}</p>", null, "prepare 422" },
        { "a parse error in the text", "Order", "<p>html</p>", "{{ Name | }}", "prepare 422" },
        { "no subject", null, "<p>html</p>", "text", "prepare 422" },
        { "no html", "Order", null, "text", "prepare 422" },
        { "all parts", "Order", "<p>html</p>", "text", "Order|<p>html</p>|text" },
    };

    public static TheoryData<string, string, string> RenderRows => new()
    {
        { "a loop over the lines", "{% for line in Lines %}[{{ line.Product }} x{{ line.Quantity }}]{% endfor %}", "[Pump x2][Valve x1]" },
        { "a property with a null value in a condition", "{% if Note %}note{% else %}no note{% endif %}[{{ Note }}]", "no note[]" },
        { "an unknown name", "{{ Nmae }}", "render 422" },
        { "a name in another case", "{{ name }}", "render 422" },
        { "an unknown member of a line", "{% for line in Lines %}{{ line.Prodcut }}{% endfor %}", "render 422" },
        { "a value in the html", "{{ Name }}", "&lt;b&gt;Ann&lt;/b&gt;" },
        { "the filter raw", "{{ Name | raw }}", "<b>Ann</b>" },
        { "an unknown filter", "{{ Name | upcse }}", "render 422" },
        { "a filter name in another case", "{{ Name | Upcase }}", "render 422" },
        { "an unknown filter in an assign tag", "{% assign x = Name | upcse %}{{ x }}", "render 422" },
        { "an unknown filter in a branch that does not run", "{% if Note %}{{ Note | upcse }}{% endif %}", "" },
        { "a known filter", "{{ Name | upcase }}", "&lt;B&gt;ANN&lt;/B&gt;" },
        { "a member of a type of the .NET runtime", "{{ Zone.Id }}", "render 422" },
        { "a member of a type in a namespace of the .NET runtime", "{{ Culture.Name }}", "render 422" },
        { "the time of now in UTC", "{{ 'now' | date: '%Y-%m-%d %H:%M' }}", "2030-01-01 00:00" },
        { "a date with no time zone in UTC", "{{ '2030-06-01 10:00' | date: '%H:%M %z' }}", "10:00 &#x2B;0000" },
        { "a loop of 99,990 steps", "{% for i in (1..99990) %}{% endfor %}", "" },
        { "a loop of 200,000 steps", "{% for i in (1..200000) %}{% endfor %}", "render 422" },
        { "a render tag", "{% render 'Footer' %}", "render 422" },
        { "an include tag", "{% include 'Footer' %}", "render 422" },
        { "a variable of the subject", "[{{ fromSubject }}]", "render 422" },
    };

    public static TheoryData<string, string?, string> CompanyRows => new()
    {
        { "the company 42 with a folder", "42", "Company 42 Client 1|<p>Company 42 Client 1</p>|" },
        { "a company with no folder", "acme", "Default Client 1|<p>Default Client 1</p>|" },
        { "no company", null, "Default Client 1|<p>Default Client 1</p>|" },
    };

    [Theory]
    [MemberData(nameof(PrepareRows))]
    public async Task PrepareAsync_PartsOfATemplate_GiveTheRuleOfTheParse(string row, string? subject, string? html, string? text, string expectedOutcome)
    {
        // Arrange
        testData.SeedParts(
            new TemplateLookup(Type, "en", Scope: null),
            [
                .. subject is null ? Array.Empty<TemplatePart>() : [new TemplatePart("subject", TemplatePartKind.PlainText, subject)],
                .. html is null ? Array.Empty<TemplatePart>() : [new TemplatePart("html", TemplatePartKind.Html, html)],
                .. text is null ? Array.Empty<TemplatePart>() : [new TemplatePart("text", TemplatePartKind.PlainText, text)],
            ]);

        // Act
        var outcome = await OutcomeOfAsync(Engine, new EmailTemplateRequest(Type, "en", Scope: null), APayload());

        // Assert
        Assert.Equal((row, expectedOutcome), (row, outcome));
    }

    [Theory]
    [MemberData(nameof(RenderRows))]
    public async Task RenderAsync_LiquidInTheHtml_GivesTheRuleOfTheRender(string row, string html, string expectedHtml)
    {
        // Arrange
        testData.SeedParts(
            new TemplateLookup(Type, "en", Scope: null),
            [new TemplatePart("subject", TemplatePartKind.PlainText, "{% assign fromSubject = 'x' %}S"), new TemplatePart("html", TemplatePartKind.Html, html)]);

        // Act
        var outcome = await OutcomeOfAsync(testData.LineEngine, new EmailTemplateRequest(Type, "en", Scope: null), LineOrderEmail.AnOrder("<b>Ann</b>"));

        // Assert
        Assert.Equal((row, expectedHtml.StartsWith("render", StringComparison.Ordinal) ? expectedHtml : $"S|{expectedHtml}|"), (row, outcome));
    }

    [Fact]
    public async Task RenderAsync_TemplateThatReadsFailingPayloadCode_GivesTheExceptionToTheCaller()
    {
        // Arrange
        testData.SeedParts(
            new TemplateLookup(Type, "en", Scope: null),
            [new TemplatePart("subject", TemplatePartKind.PlainText, "Order"), new TemplatePart("html", TemplatePartKind.Html, "<p>{{ Failing }}</p>")]);

        // Act
        var exception = await Record.ExceptionAsync(() =>
            OutcomeOfAsync(testData.LineEngine, new EmailTemplateRequest(Type, "en", Scope: null), LineOrderEmail.AnOrder()));

        // Assert
        Assert.IsType<InvalidOperationException>(exception);
    }

    [Fact]
    public async Task PrepareAsync_SourceWithTheKeyOfAParsedTemplate_UsesTheParsedTemplateUntilTheVersionChanges()
    {
        // Arrange
        var lookup = new TemplateLookup(Type, "en", Scope: null);
        var id = Guid.NewGuid().ToString();
        var request = new EmailTemplateRequest(Type, "en", Scope: null);

        // Act
        testData.SeedParts(lookup, PartsWithTheSubject("first"), new TemplateKey(id, 1));
        var first = await OutcomeOfAsync(Engine, request, APayload());
        testData.SeedParts(lookup, PartsWithTheSubject("second"), new TemplateKey(id, 1));
        var sameVersion = await OutcomeOfAsync(Engine, request, APayload());
        testData.SeedParts(lookup, PartsWithTheSubject("second"), new TemplateKey(id, 2));
        var newVersion = await OutcomeOfAsync(Engine, request, APayload());

        // Assert
        Assert.Equal(("first|<p>html</p>|", "first|<p>html</p>|", "second|<p>html</p>|"), (first, sameVersion, newVersion));
    }

    [Fact]
    public async Task PrepareAsync_LanguageWithNoTemplate_TakesTheTemplateOfTheDefaultLanguage()
    {
        // Arrange
        testData.SeedParts(new TemplateLookup(Type, "en", Scope: null), PartsWithTheSubject("English"));

        // Act
        var outcome = await OutcomeOfAsync(Engine, new EmailTemplateRequest(Type, "hu", Scope: null), APayload());

        // Assert
        Assert.Equal("English|<p>html</p>|", outcome);
    }

    [Theory]
    [MemberData(nameof(CompanyRows))]
    public async Task PrepareAsync_RequestOfACompany_TakesTheTemplateOfTheFolderOfTheCompany(string row, string? company, string expectedOutcome)
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatch(static o => o.DefaultLanguage = "en")
            .UseEmbeddedTemplates(typeof(LiquidTemplateTestData).Assembly, LiquidTemplateTestData.Prefix)
            .AddTestEvent(static m => m.WithPayload<OrderEmail, TestOrderLoader>(static p => p.AsLiquidEmail("OrderEmail")));
        await using var provider = services.Build();
        await using var scope = provider.CreateAsyncScope();
        var engine = scope.ServiceProvider.GetRequiredService<IEmailTemplateEngine<OrderEmail>>();

        // Act
        var outcome = await OutcomeOfAsync(engine, new EmailTemplateRequest(new MessageType("Company"), "en", company), APayload());

        // Assert
        Assert.Equal((row, expectedOutcome), (row, outcome));
    }

    [Fact]
    public async Task PrepareAsync_EngineOfATenant_TakesTheTemplateFromTheTemplateStoreOfTheTenant()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatch(static o => o.DefaultLanguage = "en")
            .UseTemplateStore(_ => AStoreWithTheSubject("Shared subject"))
            .UseTenantTemplateStore((_, _) => AStoreWithTheSubject("Tenant subject"))
            .AddTestEvent(static m => m.WithPayload<OrderEmail, TestOrderLoader>(static p => p.AsLiquidEmail("OrderEmail")));
        await using var provider = services.Build();
        await using var scope = provider.CreateAsyncScope();
        var engine = scope.ServiceProvider.GetRequiredKeyedService<IEmailTemplateEngine<OrderEmail>>(TenantKey.For(new TenantId("t1")));

        // Act
        var outcome = await OutcomeOfAsync(engine, new EmailTemplateRequest(Type, "en", Scope: null), APayload());

        // Assert
        Assert.Equal("Tenant subject|<p>Client 1</p>|", outcome);
    }

    private static TemplatePart[] PartsWithTheSubject(string subject) =>
        [new TemplatePart("subject", TemplatePartKind.PlainText, subject), new TemplatePart("html", TemplatePartKind.Html, "<p>html</p>")];

    private static ITemplateStore AStoreWithTheSubject(string subject)
    {
        var store = Substitute.For<ITemplateStore>();
        store.FindAsync(default, default).ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<TemplateSource?>.ForValue(new TemplateSource(
            new TemplateKey(Guid.NewGuid().ToString(), 0),
            [new TemplatePart("subject", TemplatePartKind.PlainText, subject), new TemplatePart("html", TemplatePartKind.Html, "<p>{{ Name }}</p>")]))));
        return store;
    }

    private static async Task<string> OutcomeOfAsync<TPayload>(IEmailTemplateEngine<TPayload> engine, EmailTemplateRequest request, TPayload payload)
    {
        var template = await engine.PrepareAsync(request, Ct);
        if (template.IsError)
        {
            return $"prepare {template.Error.Type.Code}";
        }

        var rendered = await template.Value.RenderAsync(
            payload, TestRoutes.AnEmailRoute(language: request.Language), CultureInfo.GetCultureInfo(request.Language), Ct);
        return rendered.IsError
            ? $"render {rendered.Error.Type.Code}"
            : $"{rendered.Value.Subject}|{rendered.Value.HtmlBody}|{rendered.Value.TextBody}";
    }
}
