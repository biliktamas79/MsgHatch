using System.Collections.Concurrent;
using System.Reflection;
using MsgHatch.Domain.Failures;

namespace MsgHatch.Email.Templates;

/// <summary>
/// The template store of the embedded resources of an assembly (section 4.10). It reads the resources
/// <c>{prefix}.{file name}</c>, with the file names of <see cref="FileSystemTemplateStore"/>. The template id is
/// <c>{assembly name}:{resource name}</c> with no part suffix, and the version is 0, because a resource does not change
/// while the process runs. So it caches its sources and its misses for the life of the process. It is a shared
/// template store only, because the resources are the same for all tenants.
/// </summary>
public sealed class EmbeddedResourceTemplateStore : ITemplateStore
{
    private readonly Assembly _assembly;
    private readonly string _prefix;
    private readonly ConcurrentDictionary<TemplateLookup, TemplateSource?> _sources = new();

    /// <summary>Makes the store of the embedded resources of an assembly.</summary>
    /// <param name="assembly">The assembly with the resources.</param>
    /// <param name="prefix">The prefix of the resource names, for example <c>ExampleApp.Templates</c>.</param>
    public EmbeddedResourceTemplateStore(Assembly assembly, string prefix)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        _assembly = assembly;
        _prefix = prefix;
    }

    /// <inheritdoc/>
    public ValueTask<ErrorOr<TemplateSource?>> FindAsync(TemplateLookup lookup, CancellationToken ct)
    {
        try
        {
            return ValueTask.FromResult(ErrorOr<TemplateSource?>.ForValue(_sources.GetOrAdd(lookup, Read)));
        }
        catch (Exception exception) when (exception is IOException or BadImageFormatException)
        {
            return ValueTask.FromResult<ErrorOr<TemplateSource?>>(new Error(Errors.Unavailable, exception.Message, exception));
        }
    }

    private TemplateSource? Read(TemplateLookup lookup)
    {
        var name = $"{_prefix}.{TemplateFiles.BaseName(lookup)}";
        var parts = new List<TemplatePart>();
        foreach (var (partName, kind, suffix) in TemplateFiles.Parts)
        {
            using var stream = _assembly.GetManifestResourceStream(name + suffix);
            if (stream is not null)
            {
                using var reader = new StreamReader(stream);
                parts.Add(new TemplatePart(partName, kind, reader.ReadToEnd()));
            }
        }

        return parts.Count == 0 ? null : new TemplateSource(new TemplateKey($"{_assembly.GetName().Name}:{name}", 0), parts);
    }
}
