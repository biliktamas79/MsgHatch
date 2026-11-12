using BenchmarkDotNet.Attributes;
using MsgHatch.Domain.Failures;
using MsgHatch.Email.Templates;

namespace MsgHatch.Email.Tests.Performance;

/// <summary>A lookup of the embedded resource template store with a hit of its source cache (section 4.11).</summary>
[MemoryDiagnoser]
public class EmbeddedResourceTemplateStorePerfTests
{
    private static readonly TemplateLookup _lookup = new(PerfTemplateStore.Type, "en", Scope: null);

    private EmbeddedResourceTemplateStore _store = null!;

    [GlobalSetup]
    public void Setup()
    {
        _store = new EmbeddedResourceTemplateStore(typeof(EmbeddedResourceTemplateStorePerfTests).Assembly, "MsgHatch.Email.Tests.Performance.Templates");
        _ = _store.FindAsync(_lookup, default).GetAwaiter().GetResult().Value ?? throw new InvalidOperationException("The embedded template is missing.");
    }

    [Benchmark]
    public ErrorOr<TemplateSource?> FindAsync_SourceCacheHit() => _store.FindAsync(_lookup, default).GetAwaiter().GetResult();
}
