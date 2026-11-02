using MsgHatch.Domain.Failures;
using MsgHatch.Email.Templates;
using NSubstitute;

namespace MsgHatch.Email.Tests.Unit.TestTypes;

/// <summary>
/// Shared by the fallback tests of D123: a substitute for the tenant template store and one for the shared template
/// store. Each one gives a template for each seeded lookup <c>{store}:{language}:{scope}</c>, with the lookup as its
/// subject, and records each lookup in the same form.
/// </summary>
public sealed class FallbackStores
{
    private readonly HashSet<string> _seeded;

    public FallbackStores(IEnumerable<string> seeded)
    {
        _seeded = [.. seeded];
        Tenant = StoreNamed("tenant");
        Shared = StoreNamed("shared");
    }

    public ITemplateStore Tenant { get; }

    public ITemplateStore Shared { get; }

    public List<string> Lookups { get; } = [];

    public Error? TenantError { get; set; }

    private ITemplateStore StoreNamed(string name)
    {
        var store = Substitute.For<ITemplateStore>();
        store.FindAsync(default, default).ReturnsForAnyArgs(call =>
        {
            var lookup = call.ArgAt<TemplateLookup>(0);
            var text = $"{name}:{lookup.Language}:{lookup.Scope}";
            Lookups.Add(text);
            return ValueTask.FromResult<ErrorOr<TemplateSource?>>(name == "tenant" && TenantError is not null ? TenantError : ResultOf(text));
        });
        return store;
    }

    private ErrorOr<TemplateSource?> ResultOf(string text) =>
        ErrorOr<TemplateSource?>.ForValue(
            _seeded.Contains(text)
                ? PlaceholderTemplateTestData.ASource([new TemplatePart("subject", TemplatePartKind.PlainText, text), new TemplatePart("html", TemplatePartKind.Html, "<p>html</p>")])
                : null);
}
