using System.Collections.Concurrent;
using Fluid;
using MsgHatch.Domain.Failures;
using MsgHatch.Email.Templates;

namespace MsgHatch.Email.Liquid;

/// <summary>The parsed parts of one template version.</summary>
/// <param name="Subject">The required part <c>subject</c>.</param>
/// <param name="Html">The required part <c>html</c>.</param>
/// <param name="Text">The optional part <c>text</c>, or null.</param>
internal sealed record LiquidParts(IFluidTemplate Subject, IFluidTemplate Html, IFluidTemplate? Text);

/// <summary>
/// The parsed Liquid templates (D210). Each <see cref="TemplateKey"/> is parsed one time for the life of the process.
/// A template with a parse error or a missing part is parsed again on each lookup, so that its error stays visible.
/// </summary>
internal static class LiquidTemplateCache
{
    private static readonly FluidParser _parser = new();

    // ponytail: unbounded cache, add a size limit if a store produces many versions
    private static readonly ConcurrentDictionary<TemplateKey, LiquidParts> _templates = new();

    public static ErrorOr<LiquidParts> GetOrParse(TemplateSource source) =>
        _templates.TryGetValue(source.Key, out var cached) ? cached : Parse(source);

    private static ErrorOr<LiquidParts> Parse(TemplateSource source)
    {
        var subject = ParsePart(source, "subject");
        var html = ParsePart(source, "html");
        var text = ParsePart(source, "text");
        if ((subject.Error ?? html.Error ?? text.Error) is { } error)
        {
            return error;
        }

        if (subject.Value is null || html.Value is null)
        {
            return Invalid($"The template {source.Key.Id} has no {(subject.Value is null ? "subject" : "html")} part.");
        }

        return _templates.GetOrAdd(source.Key, new LiquidParts(subject.Value, html.Value, text.Value));
    }

    private static ErrorOr<IFluidTemplate?> ParsePart(TemplateSource source, string name)
    {
        if (source.Parts.FirstOrDefault(part => part.Name == name) is not { } part)
        {
            return ErrorOr<IFluidTemplate?>.ForValue(null);
        }

        return _parser.TryParse(part.Text, out var template, out var error)
            ? ErrorOr<IFluidTemplate?>.ForValue(template)
            : Invalid($"The {name} part of the template {source.Key.Id} is not valid Liquid: {error}");
    }

    private static Error Invalid(string message) => new(Errors.TemplateInvalid, message);
}
