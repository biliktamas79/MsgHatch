using System.Globalization;
using Microsoft.Extensions.Options;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Email.Engines;
using MsgHatch.Email.Templates;
using MsgHatch.Email.Tests.Unit.Contracts;
using MsgHatch.Email.Tests.Unit.TestTypes;
using NSubstitute;

namespace MsgHatch.Email.Tests.Unit.Engines;

/// <summary>
/// The placeholder engine: the use cases of the engine contract, and the rules that only this engine has: the parser
/// rules, the required parts, the cache of the parsed templates, and the fallback of D123 (section 4.10).
/// </summary>
public sealed class PlaceholderEmailEngineUnitTests(PlaceholderTemplateTestData testData)
    : EmailTemplateEngineTestsBase<PlaceholderEmailEngine<OrderEmail>>(
        new PlaceholderEmailEngine<OrderEmail>(tenantStore: null, testData.Store, Options.Create(new MsgHatchOptions())), testData),
        IClassFixture<PlaceholderTemplateTestData>
{
    private const string _html = "<p>html</p>";
    private static readonly TenantKey _t1 = TenantKey.For(new TenantId("t1"));

    private static readonly string[] _tenantWithScope =
        ["tenant:hu:acme", "tenant:hu:", "shared:hu:acme", "shared:hu:", "tenant:en:acme", "tenant:en:", "shared:en:acme", "shared:en:"];

    public static TheoryData<string, string, string> ParserRows => new()
    {
        { "literal text", "Order shipped", "Order shipped" },
        { "one placeholder", "Hello {{Name}}", "Hello Client 1" },
        { "two placeholders", "{{Name}}: {{Count}}", "Client 1: 3" },
        { "a placeholder at the start and the end", "{{Count}}x{{Count}}", "3x3" },
        { "a name with an underscore and a digit", "[{{Code_1}}]", "[C1]" },
        { "a null nullable property", "[{{Note}}]", "[]" },
        { "closing braces outside a placeholder", "a }} b", "a }} b" },
        { "single braces", "a { b } c", "a { b } c" },
        { "an unclosed placeholder", "Hello {{Name", "422" },
        { "a name with a space", "{{ Name }}", "422" },
        { "a name with a dash", "{{Na-me}}", "422" },
        { "an empty name", "{{}}", "422" },
        { "three braces", "{{{Name}}}", "422" },
        { "an unknown name", "{{Unknown}}", "422" },
        { "a name in another case", "{{name}}", "422" },
    };

    public static TheoryData<string, string[]> MissingPartRows => new()
    {
        { "no subject", ["html", "text"] },
        { "no html", ["subject", "text"] },
        { "only a text", ["text"] },
    };

    public static TheoryData<string, bool, string?, string[], string> FallbackRows => new()
    {
        { "tenant, scope, lookup 1", true, "acme", _tenantWithScope, "tenant:hu:acme" },
        { "tenant, scope, lookup 2", true, "acme", _tenantWithScope[1..], "tenant:hu:" },
        { "tenant, scope, lookup 3", true, "acme", _tenantWithScope[2..], "shared:hu:acme" },
        { "tenant, scope, lookup 4", true, "acme", _tenantWithScope[3..], "shared:hu:" },
        { "tenant, scope, lookup 5", true, "acme", _tenantWithScope[4..], "tenant:en:acme" },
        { "tenant, scope, lookup 6", true, "acme", _tenantWithScope[5..], "tenant:en:" },
        { "tenant, scope, lookup 7", true, "acme", _tenantWithScope[6..], "shared:en:acme" },
        { "tenant, scope, lookup 8", true, "acme", _tenantWithScope[7..], "shared:en:" },
        { "no tenant, scope, lookup 1", false, "acme", _tenantWithScope, "shared:hu:acme" },
        { "no tenant, scope, lookup 2", false, "acme", ["tenant:hu:acme", "tenant:hu:", "shared:hu:", "shared:en:acme", "shared:en:"], "shared:hu:" },
        { "no tenant, scope, lookup 3", false, "acme", ["tenant:hu:acme", "tenant:hu:", "shared:en:acme", "shared:en:"], "shared:en:acme" },
        { "no tenant, scope, lookup 4", false, "acme", ["tenant:hu:acme", "tenant:en:acme", "tenant:en:", "shared:en:"], "shared:en:" },
        { "tenant, no scope, lookup 1", true, null, _tenantWithScope, "tenant:hu:" },
        { "tenant, no scope, lookup 2", true, null, ["tenant:hu:acme", "shared:hu:acme", "shared:hu:", "tenant:en:", "shared:en:"], "shared:hu:" },
        { "tenant, no scope, lookup 3", true, null, ["shared:hu:acme", "tenant:en:acme", "tenant:en:", "shared:en:"], "tenant:en:" },
        { "tenant, no scope, lookup 4", true, null, ["tenant:hu:acme", "tenant:en:acme", "shared:en:acme", "shared:en:"], "shared:en:" },
        { "no tenant, no scope, lookup 1", false, null, _tenantWithScope, "shared:hu:" },
        { "no tenant, no scope, lookup 2", false, null, ["tenant:hu:", "shared:hu:acme", "tenant:en:", "shared:en:acme", "shared:en:"], "shared:en:" },
    };

    [Theory]
    [MemberData(nameof(ParserRows))]
    public async Task PrepareAsync_PlaceholderSyntax_GivesTheRuleOfTheParser(string row, string subject, string expectedOutcome)
    {
        // Arrange
        var store = AStoreWith(PlaceholderTemplateTestData.ASource([Part("subject", subject), Part("html", _html)]));

        // Act
        var outcome = await OutcomeOfAsync(EngineWith(tenantStore: null, store), new EmailTemplateRequest(Type, "en", Scope: null));

        // Assert
        Assert.Equal((row, expectedOutcome), (row, outcome));
    }

    [Theory]
    [MemberData(nameof(MissingPartRows))]
    public async Task PrepareAsync_TemplateWithNoRequiredPart_GivesTemplateInvalid(string row, string[] partNames)
    {
        // Arrange
        var store = AStoreWith(PlaceholderTemplateTestData.ASource([.. partNames.Select(static name => Part(name, "text"))]));

        // Act
        var result = await EngineWith(tenantStore: null, store).PrepareAsync(new EmailTemplateRequest(Type, "en", Scope: null), Ct);

        // Assert
        Assert.Equal((row, (int?)422), (row, CodeOf(result)));
    }

    [Theory]
    [InlineData("<p>{{Unknown}}</p>", "text")]
    [InlineData("<p>html</p>", "{{Unknown}}")]
    public async Task PrepareAsync_UnknownPlaceholderInTheHtmlOrTheTextPart_GivesTemplateInvalid(string html, string text)
    {
        // Arrange
        var store = AStoreWith(PlaceholderTemplateTestData.ASource([Part("subject", "Order"), Part("html", html), Part("text", text)]));

        // Act
        var result = await EngineWith(tenantStore: null, store).PrepareAsync(new EmailTemplateRequest(Type, "en", Scope: null), Ct);

        // Assert
        Assert.Equal(422, CodeOf(result));
    }

    [Fact]
    public async Task PrepareAsync_SourceWithTheKeyOfAParsedTemplate_UsesTheParsedTemplate()
    {
        // Arrange
        var key = new TemplateKey(Guid.NewGuid().ToString(), 7);
        var store = Substitute.For<ITemplateStore>();
        store.FindAsync(default, Ct).ReturnsForAnyArgs(
            ValueTask.FromResult(ErrorOr<TemplateSource?>.ForValue(new TemplateSource(key, [Part("subject", "First"), Part("html", _html)]))),
            ValueTask.FromResult(ErrorOr<TemplateSource?>.ForValue(new TemplateSource(key, [Part("subject", "Second"), Part("html", _html)]))));
        var engine = EngineWith(tenantStore: null, store);
        await engine.PrepareAsync(new EmailTemplateRequest(Type, "en", Scope: null), Ct);

        // Act
        var outcome = await OutcomeOfAsync(engine, new EmailTemplateRequest(Type, "en", Scope: null));

        // Assert
        Assert.Equal("First", outcome);
    }

    [Fact]
    public async Task PrepareAsync_SourceWithANewVersion_ParsesItAgain()
    {
        // Arrange
        var id = Guid.NewGuid().ToString();
        var store = Substitute.For<ITemplateStore>();
        store.FindAsync(default, Ct).ReturnsForAnyArgs(
            ValueTask.FromResult(ErrorOr<TemplateSource?>.ForValue(new TemplateSource(new(id, 1), [Part("subject", "First"), Part("html", _html)]))),
            ValueTask.FromResult(ErrorOr<TemplateSource?>.ForValue(new TemplateSource(new(id, 2), [Part("subject", "Second"), Part("html", _html)]))));
        var engine = EngineWith(tenantStore: null, store);
        await engine.PrepareAsync(new EmailTemplateRequest(Type, "en", Scope: null), Ct);

        // Act
        var outcome = await OutcomeOfAsync(engine, new EmailTemplateRequest(Type, "en", Scope: null));

        // Assert
        Assert.Equal("Second", outcome);
    }

    [Fact]
    public async Task RenderAsync_LongValuesInAnHtmlPart_RenderWhole()
    {
        // Arrange
        var name = new string('<', 300);
        var store = AStoreWith(PlaceholderTemplateTestData.ASource([Part("subject", "{{Name}}"), new TemplatePart("html", TemplatePartKind.Html, "<p>{{Name}}</p>")]));
        var template = await EngineWith(tenantStore: null, store).PrepareAsync(new EmailTemplateRequest(Type, "en", Scope: null), Ct);

        // Act
        var rendered = await template.Value.RenderAsync(APayload(name: name), TestRoutes.AnEmailRoute(), CultureInfo.InvariantCulture, Ct);

        // Assert
        Assert.Equal(
            (name, "<p>" + string.Concat(Enumerable.Repeat("&lt;", 300)) + "</p>"),
            (rendered.Value.Subject, rendered.Value.HtmlBody));
    }

    [Fact]
    public async Task RenderAsync_WriterThatAsksForMemory_RendersTheValueInEachPart()
    {
        // Arrange
        var store = AStoreWith(PlaceholderTemplateTestData.ASource([Part("subject", "[{{Name}}]"), Part("html", "<p>{{Name}}</p>")]));
        var engine = new PlaceholderEmailEngine<MemoryWriterPayload>(tenantStore: null, store, Options.Create(new MsgHatchOptions { DefaultLanguage = "en" }));
        var template = await engine.PrepareAsync(new EmailTemplateRequest(Type, "en", Scope: null), Ct);

        // Act
        var rendered = await template.Value.RenderAsync(new MemoryWriterPayload("Client 1"), TestRoutes.AnEmailRoute(), CultureInfo.InvariantCulture, Ct);

        // Assert
        Assert.Equal(("[Client 1]", "<p>Client 1</p>"), (rendered.Value.Subject, rendered.Value.HtmlBody));
    }

    [Theory]
    [MemberData(nameof(FallbackRows))]
    public async Task PrepareAsync_TemplatesOfTheFallbackFromTheContainer_GivesTheFirstTemplateInTheOrderOfD123(
        string row, bool withTenant, string? scope, string[] seeded, string expectedTemplate)
    {
        // Arrange
        var stores = new FallbackStores(seeded);
        await using var host = new EngineHost(stores.Tenant, stores.Shared);

        // Act
        var outcome = await OutcomeOfAsync(host.Engine(withTenant ? _t1 : TenantKey.None), new EmailTemplateRequest(Type, "hu", scope));

        // Assert
        Assert.Equal((row, expectedTemplate), (row, outcome));
    }

    [Fact]
    public async Task PrepareAsync_NoTemplate_LooksUpTheEightTemplatesInTheOrderOfD123AndGivesNotFound()
    {
        // Arrange
        var stores = new FallbackStores([]);
        await using var host = new EngineHost(stores.Tenant, stores.Shared);

        // Act
        var result = await host.Engine(_t1).PrepareAsync(new EmailTemplateRequest(Type, "hu", "acme"), Ct);

        // Assert
        Assert.Equal(404, CodeOf(result));
        Assert.Equal(_tenantWithScope, stores.Lookups);
    }

    [Fact]
    public async Task PrepareAsync_TenantKeyAndNoTenantTemplateStore_LooksUpOnlyTheSharedStore()
    {
        // Arrange
        var stores = new FallbackStores([]);
        await using var host = new EngineHost(tenantStore: null, stores.Shared);

        // Act
        await host.Engine(_t1).PrepareAsync(new EmailTemplateRequest(Type, "hu", "acme"), Ct);

        // Assert
        Assert.Equal(["shared:hu:acme", "shared:hu:", "shared:en:acme", "shared:en:"], stores.Lookups);
    }

    [Fact]
    public async Task PrepareAsync_RequestInTheDefaultLanguage_LooksUpEachTemplateOneTime()
    {
        // Arrange
        var stores = new FallbackStores([]);
        await using var host = new EngineHost(stores.Tenant, stores.Shared);

        // Act
        await host.Engine(_t1).PrepareAsync(new EmailTemplateRequest(Type, "en", "acme"), Ct);

        // Assert
        Assert.Equal(["tenant:en:acme", "tenant:en:", "shared:en:acme", "shared:en:"], stores.Lookups);
    }

    [Fact]
    public async Task PrepareAsync_StoreError_GivesTheErrorAndLooksUpNoOtherTemplate()
    {
        // Arrange
        var error = new Error(Errors.Unavailable);
        var stores = new FallbackStores(_tenantWithScope) { TenantError = error };
        await using var host = new EngineHost(stores.Tenant, stores.Shared);

        // Act
        var result = await host.Engine(_t1).PrepareAsync(new EmailTemplateRequest(Type, "hu", "acme"), Ct);

        // Assert
        Assert.Same(error, result.Error);
        Assert.Equal(["tenant:hu:acme"], stores.Lookups);
    }

    private static PlaceholderEmailEngine<OrderEmail> EngineWith(ITemplateStore? tenantStore, ITemplateStore sharedStore) =>
        new(tenantStore, sharedStore, Options.Create(new MsgHatchOptions { DefaultLanguage = "en" }));

    private static ITemplateStore AStoreWith(TemplateSource source)
    {
        var store = Substitute.For<ITemplateStore>();
        store.FindAsync(default, Ct).ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<TemplateSource?>.ForValue(source)));
        return store;
    }

    private static TemplatePart Part(string name, string text) =>
        new(name, name == "html" ? TemplatePartKind.Html : TemplatePartKind.PlainText, text);

    private static async Task<string> OutcomeOfAsync(IEmailTemplateEngine<OrderEmail> engine, EmailTemplateRequest request)
    {
        var template = await engine.PrepareAsync(request, Ct);
        if (template.IsError)
        {
            return template.Error.Type.Code.ToString(CultureInfo.InvariantCulture);
        }

        var rendered = await template.Value.RenderAsync(
            new OrderEmail("Client 1", 3, 1234.5m, ShippedAt, Note: null, Code_1: "C1"), TestRoutes.AnEmailRoute(), CultureInfo.InvariantCulture, Ct);
        return rendered.Value.Subject;
    }
}
