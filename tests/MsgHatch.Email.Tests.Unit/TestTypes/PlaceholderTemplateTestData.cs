using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Email.Templates;
using MsgHatch.Email.Tests.Unit.Contracts;
using NSubstitute;

namespace MsgHatch.Email.Tests.Unit.TestTypes;

/// <summary>
/// The test data of the placeholder engine, as an xUnit class fixture: a template store substitute that gives the
/// seeded templates, each with an id of its own, because the parsed template cache is static.
/// </summary>
public sealed partial class PlaceholderTemplateTestData : IEmailTemplateTestData
{
    private readonly ConcurrentDictionary<TemplateLookup, TemplateSource> _templates = new();

    public PlaceholderTemplateTestData()
    {
        Store = Substitute.For<ITemplateStore>();
        Store.FindAsync(default, default).ReturnsForAnyArgs(call =>
            ValueTask.FromResult(ErrorOr<TemplateSource?>.ForValue(_templates.GetValueOrDefault(call.ArgAt<TemplateLookup>(0)))));
    }

    public ITemplateStore Store { get; }

    public void Seed(MessageType type, string language, string subject, string html, string? text) =>
        _templates[new TemplateLookup(type, language, Scope: null)] = ASource(
            [
                new TemplatePart("subject", TemplatePartKind.PlainText, InPlaceholderSyntax(subject)),
                new TemplatePart("html", TemplatePartKind.Html, InPlaceholderSyntax(html)),
                .. text is null ? Array.Empty<TemplatePart>() : [new TemplatePart("text", TemplatePartKind.PlainText, InPlaceholderSyntax(text))],
            ]);

    public static TemplateSource ASource(IReadOnlyList<TemplatePart> parts) =>
        new(new TemplateKey(Guid.NewGuid().ToString(), 0), parts);

    private static string InPlaceholderSyntax(string text) => Placeholder().Replace(text, "{{$1}}");

    [GeneratedRegex(@"\{(\w+)\}")]
    private static partial Regex Placeholder();
}
