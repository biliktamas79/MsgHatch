using System.Collections.Concurrent;
using System.Reflection;
using MsgHatch.Domain.Failures;

namespace MsgHatch.Templates;

/// <summary>
/// The template store of the embedded resources of an assembly (section 4.10). It reads the resources
/// <c>{prefix}.{file name}</c>, with the file names of <see cref="FileSystemTemplateStore"/>, and the template of a scope
/// as <c>{prefix}.{segment}.{file name}</c> with the segment of <see cref="ScopeFolders.SegmentOf"/>. These are the
/// names that MSBuild gives to the files of a folder and its scope folders. The template id is
/// <c>{assembly name}:{resource name}</c>, and the version is 0, because a resource does not change
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
        var name = TemplateFiles.ResourceNameOf(_prefix, lookup);
        if (name is null)
        {
            return null;
        }

        using var stream = _assembly.GetManifestResourceStream(name);
        if (stream is null)
        {
            return null;
        }

        using var reader = new StreamReader(stream);
        return new TemplateSource(new TemplateKey($"{_assembly.GetName().Name}:{name}", 0), reader.ReadToEnd());
    }
}
