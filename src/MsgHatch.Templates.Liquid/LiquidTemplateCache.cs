using System.Collections.Concurrent;
using Fluid;
using MsgHatch.Domain.Failures;
using MsgHatch.Templates;

namespace MsgHatch.Templates.Liquid;

/// <summary>
/// The parsed Liquid templates (D210). Each <see cref="TemplateKey"/> is parsed one time for the life of the process.
/// A text with a parse error is parsed again on each lookup, so that its error stays visible.
/// </summary>
internal static class LiquidTemplateCache
{
    private static readonly FluidParser _parser = new();

    // ponytail: unbounded cache, add a size limit if a store produces many versions
    private static readonly ConcurrentDictionary<TemplateKey, IFluidTemplate> _templates = new();

    public static ErrorOr<IFluidTemplate> GetOrParse(TemplateSource source)
    {
        if (_templates.TryGetValue(source.Key, out var cached))
        {
            return ErrorOr<IFluidTemplate>.ForValue(cached);
        }

        var parsed = Parse(source);
        return parsed.IsError ? parsed : ErrorOr<IFluidTemplate>.ForValue(_templates.GetOrAdd(source.Key, parsed.Value));
    }

    /// <summary>Parses a text with no cache entry, for the check before a write (D219).</summary>
    public static ErrorOr<IFluidTemplate> Parse(TemplateSource source) =>
        _parser.TryParse(source.Text, out var template, out var error)
            ? ErrorOr<IFluidTemplate>.ForValue(template)
            : new Error(Errors.TemplateInvalid, $"The template {source.Key.Id} is not valid Liquid: {error}");
}
