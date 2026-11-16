using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Templates.DependencyInjection;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Email.DependencyInjection;
using MsgHatch.Templates;
using MsgHatch.Email.Liquid.DependencyInjection;
using MsgHatch.Email.Liquid.Tests.Unit.TestTypes;
using MsgHatch.Templates.Tests.Unit.Contracts;
using MsgHatch.Email.Tests.Unit.TestTypes;
using NSubstitute;
using MsgHatch.Templates.Tests.Unit.TestTypes;

namespace MsgHatch.Email.Liquid.Tests.Unit;

/// <summary>
/// The Liquid engine: the use cases of the engine contract, and the rules that only this engine has: the parse at
/// prepare, the unknown names (D207), the partials (D208), the model (D209), the render (D210), the unknown filters
/// (D211), the cache of the parsed templates, and the lookups of D123 and D199 for one part (D214). The engine class is
/// internal, so the test data builds it with <c>AsLiquidEmail</c>.
/// </summary>
public sealed class LiquidTemplateEngineUnitTests(LiquidTemplateTestData testData)
    : TemplateEngineTestsBase<ITemplateEngine<OrderEmail>>(testData.Engine, testData), IClassFixture<LiquidTemplateTestData>
{
    public static TheoryData<string, string, string> PrepareRows => new()
    {
        { "an unclosed output", "Hello {{ Name ", "prepare 422" },
        { "an unclosed tag", "<p>{% if Note %}</p>", "prepare 422" },
        { "a filter with no name", "{{ Name | }}", "prepare 422" },
        { "valid Liquid", "Order {{ Name }}", "Order Client 1" },
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
    };

    public static TheoryData<string, string?, string> CompanyRows => new()
    {
        { "the company 42 with a folder", "42", "Company 42 Client 1|<p>Company 42 Client 1</p>" },
        { "a company with no folder", "acme", "Default Client 1|<p>Default Client 1</p>" },
        { "no company", null, "Default Client 1|<p>Default Client 1</p>" },
    };

    [Theory]
    [MemberData(nameof(PrepareRows))]
    public async Task PrepareAsync_TextOfAPart_GivesTheRuleOfTheParse(string row, string text, string expectedOutcome)
    {
        // Arrange
        testData.SeedText(new TemplateLookup(Type, "en", Scope: null, Part, TemplatePartKind.PlainText), text);

        // Act
        var outcome = await OutcomeOfAsync(Engine, ARequest(), APayload());

        // Assert
        Assert.Equal((row, expectedOutcome), (row, outcome));
    }

    [Theory]
    [MemberData(nameof(RenderRows))]
    public async Task RenderAsync_LiquidInAnHtmlPart_GivesTheRuleOfTheRender(string row, string html, string expectedOutcome)
    {
        // Arrange
        testData.SeedText(new TemplateLookup(Type, "en", Scope: null, Part, TemplatePartKind.Html), html);

        // Act
        var outcome = await OutcomeOfAsync(testData.LineEngine, ARequest(kind: TemplatePartKind.Html), LineOrderEmail.AnOrder("<b>Ann</b>"));

        // Assert
        Assert.Equal((row, expectedOutcome), (row, outcome));
    }

    [Fact]
    public async Task RenderAsync_TemplateThatReadsFailingPayloadCode_GivesTheExceptionToTheCaller()
    {
        // Arrange
        testData.SeedText(new TemplateLookup(Type, "en", Scope: null, Part, TemplatePartKind.Html), "<p>{{ Failing }}</p>");

        // Act
        var exception = await Record.ExceptionAsync(() =>
            OutcomeOfAsync(testData.LineEngine, ARequest(kind: TemplatePartKind.Html), LineOrderEmail.AnOrder()));

        // Assert
        Assert.IsType<InvalidOperationException>(exception);
    }

    [Fact]
    public async Task PrepareAsync_SourceWithTheKeyOfAParsedTemplate_UsesTheParsedTemplateUntilTheVersionChanges()
    {
        // Arrange
        var lookup = new TemplateLookup(Type, "en", Scope: null, Part, TemplatePartKind.PlainText);
        var id = Guid.NewGuid().ToString();

        // Act
        testData.SeedText(lookup, "first", new TemplateKey(id, 1));
        var first = await OutcomeOfAsync(Engine, ARequest(), APayload());
        testData.SeedText(lookup, "second", new TemplateKey(id, 1));
        var sameVersion = await OutcomeOfAsync(Engine, ARequest(), APayload());
        testData.SeedText(lookup, "second", new TemplateKey(id, 2));
        var newVersion = await OutcomeOfAsync(Engine, ARequest(), APayload());

        // Assert
        Assert.Equal(("first", "first", "second"), (first, sameVersion, newVersion));
    }

    [Fact]
    public async Task PrepareAsync_LanguageWithNoTemplate_TakesTheTemplateOfTheDefaultLanguage()
    {
        // Arrange
        testData.SeedText(new TemplateLookup(Type, "en", Scope: null, Part, TemplatePartKind.PlainText), "English");

        // Act
        var outcome = await OutcomeOfAsync(Engine, ARequest("hu"), APayload());

        // Assert
        Assert.Equal("English", outcome);
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
        var engine = scope.ServiceProvider.GetRequiredService<ITemplateEngine<OrderEmail>>();
        var subject = new TemplateRequest(new MessageType("Company"), "en", company, "subject", TemplatePartKind.PlainText);

        // Act
        var outcome = $"{await OutcomeOfAsync(engine, subject, APayload())}|{await OutcomeOfAsync(engine, subject with { Part = "body", Kind = TemplatePartKind.Html }, APayload())}";

        // Assert
        Assert.Equal((row, expectedOutcome), (row, outcome));
    }

    [Fact]
    public async Task PrepareAsync_EngineOfATenant_TakesTheTemplateFromTheTemplateStoreOfTheTenant()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatch(static o => o.DefaultLanguage = "en")
            .UseTemplateStore(_ => AStoreWithTheText("Shared text"))
            .UseTenantTemplateStore((_, _) => AStoreWithTheText("Tenant text"))
            .AddTestEvent(static m => m.WithPayload<OrderEmail, TestOrderLoader>(static p => p.AsLiquidEmail("OrderEmail")));
        await using var provider = services.Build();
        await using var scope = provider.CreateAsyncScope();
        var engine = scope.ServiceProvider.GetRequiredKeyedService<ITemplateEngine<OrderEmail>>(TenantKey.For(new TenantId("t1")));

        // Act
        var outcome = await OutcomeOfAsync(engine, ARequest(), APayload());

        // Assert
        Assert.Equal("Tenant text", outcome);
    }

    private static ITemplateStore AStoreWithTheText(string text)
    {
        var store = Substitute.For<ITemplateStore>();
        store.FindAsync(default, default).ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<TemplateSource?>.ForValue(
            new TemplateSource(new TemplateKey(Guid.NewGuid().ToString(), 0), text))));
        return store;
    }

    private static async Task<string> OutcomeOfAsync<TPayload>(ITemplateEngine<TPayload> engine, TemplateRequest request, TPayload payload)
    {
        var template = await engine.PrepareAsync(request, Ct);
        if (template.IsError)
        {
            return $"prepare {template.Error.Type.Code}";
        }

        var rendered = await template.Value.RenderAsync(payload, CultureInfo.GetCultureInfo(request.Language), Ct);
        return rendered.IsError ? $"render {rendered.Error.Type.Code}" : rendered.Value;
    }
}
