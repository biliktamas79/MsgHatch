using MsgHatch.Email.Tests.Unit.Contracts;

namespace MsgHatch.Email.Tests.Unit.TestTypes;

/// <summary>
/// The temporary folder of the file store tests, as an xUnit class fixture. It gets the files of the folder
/// <c>TemplateFiles</c> from the embedded resources of this project, and is deleted at the end.
/// </summary>
public sealed class TemplateFolder : ITemplateStoreTestData, IDisposable
{
    private readonly string _parent = Path.Combine(Path.GetTempPath(), "msghatch-tests", Guid.NewGuid().ToString("N"));

    public TemplateFolder()
    {
        Root = Path.Combine(_parent, "templates");
        Directory.CreateDirectory(Root);
    }

    /// <summary>The root folder of the store.</summary>
    public string Root { get; }

    /// <summary>The folder that contains the root folder.</summary>
    public string Parent => _parent;

    public async ValueTask SeedAsync(CancellationToken ct)
    {
        foreach (var (fileName, resourceName) in EmbeddedTemplateFiles.Names().Where(names => !File.Exists(Path.Combine(Root, names.FileName))))
        {
            await using var resource = typeof(TemplateFolder).Assembly.GetManifestResourceStream(resourceName)!;
            await using var file = File.Create(Path.Combine(Root, fileName));
            await resource.CopyToAsync(file, ct);
        }
    }

    public void Write(string fileName, string text, DateTime lastWriteUtc, string? folder = null)
    {
        var path = Path.Combine(folder ?? Root, fileName);
        File.WriteAllText(path, text);
        File.SetLastWriteTimeUtc(path, lastWriteUtc);
    }

    public void Dispose() => Directory.Delete(_parent, recursive: true);
}
