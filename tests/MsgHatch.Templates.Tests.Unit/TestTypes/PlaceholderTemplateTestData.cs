using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Templates;
using MsgHatch.Templates.Tests.Unit.Contracts;
using NSubstitute;

namespace MsgHatch.Templates.Tests.Unit.TestTypes;

/// <summary>
/// The test data of the placeholder engine, as an xUnit class fixture: a template store substitute that gives the
/// seeded parts, each with an id of its own, because the parsed template cache is static.
/// </summary>
public sealed partial class PlaceholderTemplateTestData : ITemplateTestData
{
    private readonly ConcurrentDictionary<TemplateLookup, TemplateSource> _templates = new();

    public PlaceholderTemplateTestData()
    {
        Store = Substitute.For<ITemplateStore>();
        Store.FindAsync(default, default).ReturnsForAnyArgs(call =>
            ValueTask.FromResult(ErrorOr<TemplateSource?>.ForValue(_templates.GetValueOrDefault(call.ArgAt<TemplateLookup>(0)))));
    }

    public ITemplateStore Store { get; }

    public void Seed(MessageType type, string language, string part, TemplatePartKind kind, string text) =>
        SeedText(new TemplateLookup(type, language, Scope: null, part, kind), InPlaceholderSyntax(text));

    /// <summary>Puts a text with no change into the store, with a new key or the given key.</summary>
    public void SeedText(TemplateLookup lookup, string text, TemplateKey? key = null) =>
        _templates[lookup] = new TemplateSource(key ?? new TemplateKey(Guid.NewGuid().ToString(), 0), text);

    public static TemplateSource ASource(string text) => new(new TemplateKey(Guid.NewGuid().ToString(), 0), text);

    public string InEngineSyntax(string text) => InPlaceholderSyntax(text);

    private static string InPlaceholderSyntax(string text) => Placeholder().Replace(text, "{{$1}}");

    [GeneratedRegex(@"\{(\w+)\}")]
    private static partial Regex Placeholder();
}
