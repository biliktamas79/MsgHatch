using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using MsgHatch.Domain.Failures;
using MsgHatch.Templates;
using MsgHatch.Templates.Tests.Unit.Contracts;
using MsgHatch.Templates.Tests.Unit.TestTypes;

namespace MsgHatch.Templates.Tests.Unit;

/// <summary>
/// The writer of the file template store (D218, D223): the contract on an empty temporary folder, and the rules of the
/// files: the escaped company folders (D201), the version after a save, the move of a temporary file, and the names that
/// a list skips.
/// </summary>
public sealed class FileSystemTemplateStoreWriterUnitTests(TemplateFolder folder)
    : TemplateWriterTestsBase<FileSystemTemplateStore>(new FileSystemTemplateStore(folder.Root)), IClassFixture<TemplateFolder>
{
    public static TheoryData<string, string> CompanyFolderRows => new()
    {
        { "acme-1", "acme-1" },
        { "ACME", "^a^c^m^e" },
        { "..", "%2E%2E" },
        { "../outside", "%2E%2E%2^foutside" },
        { "c:\\outside", "c%3^a%5^coutside" },
        { "0b6c1f7e-2a44-4d8e-9a3b-61c0f5e2d7a9", "0b6c1f7e-2a44-4d8e-9a3b-61c0f5e2d7a9" },
    };

    [Fact]
    public async Task SaveAsync_FileWithALastWriteTimeInTheFuture_GivesTheVersionOneTickAfterThatTime()
    {
        // Arrange
        var first = await Store.SaveAsync(ALookup(), "First", 0, Ct);
        var future = new DateTime(first.Value.Version, DateTimeKind.Utc).AddHours(1);
        File.SetLastWriteTimeUtc(PathOf(), future);

        // Act
        var second = await Store.SaveAsync(ALookup(), "Second", future.Ticks, Ct);

        // Assert
        var source = await Store.FindAsync(ALookup(), Ct);
        Assert.Equal((future.Ticks + 1, future.Ticks + 1, "Second"), (second.Value.Version, source.Value?.Key.Version, source.Value?.Text));
    }

    [Theory]
    [MemberData(nameof(CompanyFolderRows))]
    public async Task SaveAsync_CompanyId_WritesIntoItsEscapedFolderInsideTheRootFolder(string company, string expectedFolder)
    {
        // Act
        await Store.SaveAsync(ALookup(scope: company), "Company", 0, Ct);

        // Assert
        Assert.Equal(
            (true, "templates"),
            (File.Exists(Path.Combine(folder.Root, expectedFolder, $"{Type.Name}.en.subject.txt")),
                Path.GetFileName(Assert.Single(Directory.EnumerateFileSystemEntries(folder.Parent)))));
    }

    [Fact]
    public async Task SaveAsync_Text_LeavesOnlyTheTemplateFileInItsFolder()
    {
        // Act
        await Store.SaveAsync(ALookup(scope: "single"), "Text", 0, Ct);

        // Assert
        Assert.Equal([$"{Type.Name}.en.subject.txt"], Directory.EnumerateFiles(Path.Combine(folder.Root, "single")).Select(Path.GetFileName));
    }

    [Fact]
    public async Task SaveAsync_FileThatAReaderHoldsOpen_GivesUnavailableAndLeavesNoTemporaryFile()
    {
        // Arrange
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows blocks a replace of a file that a reader holds open.");
        var first = await Store.SaveAsync(ALookup(scope: "open"), "First", 0, Ct);
        ErrorOr<TemplateEntry> result;

        // Act
        using (new FileStream(PathOf("open"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            result = await Store.SaveAsync(ALookup(scope: "open"), "Second", first.Value.Version, Ct);
        }

        // Assert
        var source = await Store.FindAsync(ALookup(scope: "open"), Ct);
        Assert.Equal(
            (503, "First", 1),
            (result.Error?.Type.Code, source.Value?.Text, Directory.EnumerateFiles(Path.Combine(folder.Root, "open")).Count()));
    }

    [Fact]
    public async Task SaveAsync_EmptyScope_GivesValidationFailedAndWritesNoFile()
    {
        // Act
        var result = await Store.SaveAsync(ALookup(scope: string.Empty), "Text", 0, Ct);

        // Assert
        Assert.Equal((400, false), (result.Error?.Type.Code, File.Exists(PathOf())));
    }

    [Fact]
    public async Task DeleteAsync_EmptyScope_GivesNoError()
    {
        // Act
        var error = await Store.DeleteAsync(ALookup(scope: string.Empty), Ct);

        // Assert
        Assert.Null(error);
    }

    [Fact]
    public async Task DeleteAsync_FileThatAReaderHoldsOpen_GivesUnavailableAndKeepsTheFile()
    {
        // Arrange
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows blocks a delete of a file that a reader holds open.");
        await Store.SaveAsync(ALookup(scope: "held"), "First", 0, Ct);
        Error? error;

        // Act
        using (new FileStream(PathOf("held"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            error = await Store.DeleteAsync(ALookup(scope: "held"), Ct);
        }

        // Assert
        Assert.Equal((503, true), (error?.Type.Code, File.Exists(PathOf("held"))));
    }

    [Fact]
    public async Task FindAsync_EmptyScope_GivesNull()
    {
        // Arrange
        await Store.SaveAsync(ALookup(), "No company", 0, Ct);

        // Act
        var result = await Store.FindAsync(ALookup(scope: string.Empty), Ct);

        // Assert
        Assert.Equal((false, null), (result.IsError, result.Value));
    }

    [Fact]
    public async Task ListAsync_NamesThatTheStoreDoesNotMake_AreSkipped()
    {
        // Arrange
        var kept = await Store.SaveAsync(ALookup(scope: "junk"), "Kept", 0, Ct);
        WriteJunkFiles(
            "junk",
            "readme.md", $"{Type.Name}.en.subject.txt.tmp", $".{Type.Name}.tmp", $"{Type.Name}.en.sub.ject.txt", $"{Type.Name}.en.subject.pdf",
            $"{Type.Name}..subject.txt", "1Bad.en.subject.txt", $"{Type.Name}.en%2.subject.txt");

        // Act
        var entries = await Store.ListAsync(new TemplateQuery(Scope: "junk"), Ct);

        // Assert
        Assert.Equal([kept.Value], entries.Value);
    }

    [Fact]
    public async Task ListAsync_FolderThatNoCompanyHas_IsSkipped()
    {
        // Arrange
        var company = await Store.SaveAsync(ALookup(scope: "INITECH"), "Company", 0, Ct);
        Directory.CreateDirectory(Path.Combine(folder.Root, "Initech"));
        folder.Write($"{Type.Name}.en.subject.txt", "Junk", DateTime.UnixEpoch, Path.Combine(folder.Root, "Initech"));

        // Act
        var entries = await Store.ListAsync(new TemplateQuery(Type), Ct);

        // Assert
        Assert.Equal([company.Value], entries.Value);
    }

    [Fact]
    public async Task ListAsync_MissingRootFolder_GivesNoEntry()
    {
        // Arrange
        var store = new FileSystemTemplateStore(Path.Combine(folder.Parent, "missing"));

        // Act
        var entries = await store.ListAsync(new TemplateQuery(), Ct);

        // Assert
        Assert.Empty(entries.Value);
    }

    [Fact]
    public async Task ListAsync_EmptyScope_GivesNoEntry()
    {
        // Arrange
        await Store.SaveAsync(ALookup(), "No company", 0, Ct);

        // Act
        var entries = await Store.ListAsync(new TemplateQuery(Type, string.Empty), Ct);

        // Assert
        Assert.Empty(entries.Value);
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public async Task ListAsync_CompanyFolderThatCannotBeListed_GivesUnavailable()
    {
        // Arrange
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The test denies the listing of a folder with a Windows access rule.");
        await Store.SaveAsync(ALookup(scope: "unlisted"), "Text", 0, Ct);
        using var denial = new ListingDenial(new DirectoryInfo(Path.Combine(folder.Root, "unlisted")));

        // Act
        var entries = await Store.ListAsync(new TemplateQuery(Scope: "unlisted"), Ct);

        // Assert
        Assert.Equal(503, entries.Error?.Type.Code);
    }

    private string PathOf(string? folderName = null) =>
        Path.Combine(folderName is null ? folder.Root : Path.Combine(folder.Root, folderName), $"{Type.Name}.en.subject.txt");

    private void WriteJunkFiles(string folderName, params string[] names)
    {
        foreach (var name in names)
        {
            folder.Write(name, "Junk", DateTime.UnixEpoch, Path.Combine(folder.Root, folderName));
        }
    }

    [SupportedOSPlatform("windows")]
    private sealed class ListingDenial : IDisposable
    {
        private readonly DirectoryInfo _folder;
        private readonly DirectorySecurity _security;
        private readonly FileSystemAccessRule _denial;

        public ListingDenial(DirectoryInfo folder)
        {
            using var user = WindowsIdentity.GetCurrent();
            _folder = folder;
            _denial = new FileSystemAccessRule(user.User!, FileSystemRights.ListDirectory, AccessControlType.Deny);
            _security = folder.GetAccessControl();
            _security.AddAccessRule(_denial);
            folder.SetAccessControl(_security);
        }

        public void Dispose()
        {
            _security.RemoveAccessRule(_denial);
            _folder.SetAccessControl(_security);
        }
    }
}
