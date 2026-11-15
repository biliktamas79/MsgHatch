using MsgHatch.Email.Tests.Unit.Contracts;

namespace MsgHatch.Email.Tests.Unit.TestTypes;

/// <summary>
/// The temporary folder of the file store tests, as an xUnit class fixture. It gets the files of the folder
/// <c>TemplateFiles</c>, with its company folders, from the output folder of this project, and is deleted at the end.
/// The folder that contains the root folder gets a template of <c>TestEvent</c> too, so that a name that leads out of
/// the root folder finds a file.
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

    public ValueTask SeedAsync(CancellationToken ct)
    {
        var source = Path.Combine(AppContext.BaseDirectory, "TemplateFiles");
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            CopyIfMissing(file, Path.Combine(Root, Path.GetRelativePath(source, file)));
        }

        CopyIfMissing(Path.Combine(source, "TestEvent.en.subject.txt"), Path.Combine(_parent, "TestEvent.en.subject.txt"));
        return ValueTask.CompletedTask;
    }

    public void Write(string fileName, string text, DateTime lastWriteUtc, string? folder = null)
    {
        var path = Path.Combine(folder ?? Root, fileName);
        File.WriteAllText(path, text);
        File.SetLastWriteTimeUtc(path, lastWriteUtc);
    }

    public void Dispose() => Directory.Delete(_parent, recursive: true);

    private static void CopyIfMissing(string source, string target)
    {
        if (!File.Exists(target))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(source, target);
        }
    }
}
