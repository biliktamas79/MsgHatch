using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Templates;

namespace MsgHatch.Email.Tests.Performance;

/// <summary>
/// The template store of the email benchmarks. It gives one fixed subject and one fixed html body, so each lookup after
/// the first is a hit of the parsed template cache. With <see cref="NewVersions"/>, it gives a new version of the part
/// for each lookup, from versions that it made before the measurement, so each lookup is a miss and the store
/// allocates nothing. It has no text body.
/// </summary>
public sealed class PerfTemplateStore : ITemplateStore
{
    public static readonly MessageType Type = new("Perf");

    private static readonly string[] _texts =
    [
        "Order {{Name}}: {{Count}} items",
        "<p>Hello {{Name}},</p><p>Your order has {{Count}} items for {{Amount}} EUR.</p>",
    ];

    private static readonly ValueTask<ErrorOr<TemplateSource?>> _missing = ValueTask.FromResult(ErrorOr<TemplateSource?>.ForValue(null));

    private readonly ValueTask<ErrorOr<TemplateSource?>>[][] _sources;
    private int _next;

    private PerfTemplateStore(int versions, string id)
    {
        _sources = [.. Enumerable.Range(1, versions).Select(version => (ValueTask<ErrorOr<TemplateSource?>>[])
            [.. _texts.Select((text, part) => ValueTask.FromResult(ErrorOr<TemplateSource?>.ForValue(
                new TemplateSource(new TemplateKey($"{id}:{part}", version), text))))])];
    }

    /// <summary>A store with one template, which never changes.</summary>
    public static PerfTemplateStore OneVersion() => new(1, "perf:fixed");

    /// <summary>A store that gives a new version for each of the next <paramref name="lookups"/> lookups.</summary>
    public static PerfTemplateStore NewVersions(int lookups) => new(lookups, $"perf:changing:{Guid.NewGuid():N}");

    public ValueTask<ErrorOr<TemplateSource?>> FindAsync(TemplateLookup lookup, CancellationToken ct)
    {
        var part = lookup.Part == "subject" ? 0 : lookup.Kind == TemplatePartKind.Html ? 1 : -1;
        if (part < 0)
        {
            return _missing;
        }

        return _sources.Length == 1 ? _sources[0][part] : _sources[_next++ % _sources.Length][part];
    }
}
