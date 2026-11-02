using MsgHatch.Domain.Failures;
using MsgHatch.Email.Templates;
using MsgHatch.Email.Tests.Unit.Contracts;
using MsgHatch.Email.Tests.Unit.TestTypes;

namespace MsgHatch.Email.Tests.Unit.Templates;

public sealed class FileSystemTemplateStoreUnitTests(TemplateFolder folder)
    : TemplateStoreTestsBase<FileSystemTemplateStore>(new FileSystemTemplateStore(folder.Root), folder), IClassFixture<TemplateFolder>
{
    private static readonly DateTime _firstWriteUtc = new(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task FindAsync_FileTemplate_HasTheFullPathWithNoPartSuffixAsItsIdAndTheLatestWriteTimeAsItsVersion()
    {
        // Arrange
        folder.Write("Dated.en.subject.txt", "Subject", _firstWriteUtc);
        folder.Write("Dated.en.body.html", "<p>Html</p>", _firstWriteUtc.AddMinutes(2));
        folder.Write("Dated.en.body.txt", "Text", _firstWriteUtc.AddMinutes(1));

        // Act
        var result = await Store.FindAsync(new TemplateLookup(new("Dated"), "en", Scope: null), Ct);

        // Assert
        Assert.Equal(new TemplateKey(Path.Combine(folder.Root, "Dated.en"), _firstWriteUtc.AddMinutes(2).Ticks), result.Value?.Key);
    }

    [Fact]
    public async Task FindAsync_ChangedTemplateFile_GivesANewVersionWithTheNewText()
    {
        // Arrange
        folder.Write("Changing.en.subject.txt", "First", _firstWriteUtc);
        folder.Write("Changing.en.body.html", "<p>Html</p>", _firstWriteUtc);
        var lookup = new TemplateLookup(new("Changing"), "en", Scope: null);
        var first = await Store.FindAsync(lookup, Ct);
        folder.Write("Changing.en.subject.txt", "Second", _firstWriteUtc.AddMinutes(1));

        // Act
        var second = await Store.FindAsync(lookup, Ct);

        // Assert
        Assert.Equal(
            [(_firstWriteUtc.Ticks, "First"), (_firstWriteUtc.AddMinutes(1).Ticks, "Second")],
            new[] { first.Value!, second.Value! }.Select(static source => (source.Key.Version, source.Parts[0].Text)));
    }

    [Fact]
    public async Task FindAsync_TemplateThatWasAMissAndThenExists_FindsIt()
    {
        // Arrange
        var lookup = new TemplateLookup(new("Later"), "en", Scope: null);
        var miss = await Store.FindAsync(lookup, Ct);
        folder.Write("Later.en.subject.txt", "Later", _firstWriteUtc);

        // Act
        var found = await Store.FindAsync(lookup, Ct);

        // Assert
        Assert.Equal(((string?)null, (string?)"Later"), (miss.Value?.Parts[0].Text, found.Value?.Parts[0].Text));
    }

    [Fact]
    public async Task FindAsync_ScopeWithPathCharacters_FindsTheFileWithTheEscapedScope()
    {
        // Arrange
        folder.Write("TestEvent.en.a%2Fb.subject.txt", "Escaped", _firstWriteUtc);

        // Act
        var result = await Store.FindAsync(new TemplateLookup(TestEvent, "en", "a/b"), Ct);

        // Assert
        Assert.Equal("Escaped", result.Value?.Parts[0].Text);
    }

    [Fact]
    public async Task FindAsync_ScopeThatLeadsOutOfTheRootFolder_ReadsNoFileOutsideTheRootFolder()
    {
        // Arrange
        folder.Write("secret.subject.txt", "Secret", _firstWriteUtc, folder.Parent);

        // Act
        var result = await Store.FindAsync(new TemplateLookup(TestEvent, "en", "x/../../secret"), Ct);

        // Assert
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task FindAsync_PartThatCannotBeRead_GivesUnavailable()
    {
        // Arrange
        Directory.CreateDirectory(Path.Combine(folder.Root, "Unreadable.en.subject.txt"));

        // Act
        var result = await Store.FindAsync(new TemplateLookup(new("Unreadable"), "en", Scope: null), Ct);

        // Assert
        Assert.Equal(Errors.Unavailable, result.Error?.Type);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void Constructor_NoRootPath_ThrowsArgumentException(string? rootPath)
    {
        // Arrange
        var create = () => new FileSystemTemplateStore(rootPath!);

        // Act
        var exception = Record.Exception(create);

        // Assert
        Assert.IsAssignableFrom<ArgumentException>(exception);
    }
}
