using MsgHatch.Domain.Failures;
using MsgHatch.Templates;
using MsgHatch.Templates.Tests.Unit.Contracts;
using MsgHatch.Templates.Tests.Unit.TestTypes;

namespace MsgHatch.Templates.Tests.Unit;

public sealed class FileSystemTemplateStoreUnitTests(TemplateFolder folder)
    : TemplateStoreTestsBase<FileSystemTemplateStore>(new FileSystemTemplateStore(folder.Root), folder), IClassFixture<TemplateFolder>
{
    private static readonly DateTime _firstWriteUtc = new(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task FindAsync_ScopeWithADash_GivesTheTemplateOfTheFolderOfThatScope()
    {
        // Act
        var result = await Store.FindAsync(ALookup(scope: "acme-1"), Ct);

        // Assert
        Assert.Equal("Company acme-1 order {{OrderNumber}}", result.Value?.Text);
    }

    [Fact]
    public async Task FindAsync_ScopeThatDiffersFromTheFolderOfAnotherScopeOnlyInCase_GivesNull()
    {
        // Act
        var result = await Store.FindAsync(ALookup(scope: "ACME"), Ct);

        // Assert
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task FindAsync_FileTemplate_HasTheFullPathOfItsFileAsItsIdAndTheWriteTimeOfThatFileAsItsVersion()
    {
        // Arrange
        const string subjectFile = "Dated.en.subject.txt";
        folder.Write(subjectFile, "Subject", _firstWriteUtc);
        folder.Write("Dated.en.body.html", "<p>Html</p>", _firstWriteUtc.AddMinutes(2));
        folder.Write("Dated.en.body.txt", "Text", _firstWriteUtc.AddMinutes(1));

        // Act
        var result = await Store.FindAsync(ALookup() with { Type = new("Dated") }, Ct);

        // Assert
        Assert.Equal(new TemplateKey(Path.Combine(folder.Root, subjectFile), _firstWriteUtc.Ticks), result.Value?.Key);
    }

    [Theory]
    [InlineData(638_000_000_000_000_000, "2022-09-28 22:13:20 UTC")]
    [InlineData(1, "0001-01-01 00:00:00 UTC")]
    [InlineData(0, "0")]
    [InlineData(-5, "-5")]
    [InlineData(long.MaxValue, "9223372036854775807")]
    public void RenderVersion_Version_GivesTheUtcWriteTimeOrTheNumberOutOfTheRangeOfATime(long version, string expected)
    {
        // Act
        var text = new FileSystemTemplateStore(folder.Root).RenderVersion(version);

        // Assert
        Assert.Equal(expected, text);
    }

    [Fact]
    public async Task FindAsync_ChangedTemplateFile_GivesANewVersionWithTheNewText()
    {
        // Arrange
        const string firstText = "First";
        const string secondText = "Second";
        var secondWriteUtc = _firstWriteUtc.AddMinutes(1);
        folder.Write("Changing.en.subject.txt", firstText, _firstWriteUtc);
        folder.Write("Changing.en.body.html", "<p>Html</p>", _firstWriteUtc);
        var lookup = ALookup() with { Type = new("Changing") };
        var first = await Store.FindAsync(lookup, Ct);
        folder.Write("Changing.en.subject.txt", secondText, secondWriteUtc);

        // Act
        var second = await Store.FindAsync(lookup, Ct);

        // Assert
        Assert.Equal(
            [(_firstWriteUtc.Ticks, firstText), (secondWriteUtc.Ticks, secondText)],
            new[] { first.Value!, second.Value! }.Select(static source => (source.Key.Version, source.Text)));
    }

    [Fact]
    public async Task FindAsync_TemplateThatWasAMissAndThenExists_FindsIt()
    {
        // Arrange
        const string text = "Later";
        var lookup = ALookup() with { Type = new("Later") };
        var miss = await Store.FindAsync(lookup, Ct);
        folder.Write("Later.en.subject.txt", text, _firstWriteUtc);

        // Act
        var found = await Store.FindAsync(lookup, Ct);

        // Assert
        Assert.Equal(((string?)null, (string?)text), (miss.Value?.Text, found.Value?.Text));
    }

    [Fact]
    public async Task FindAsync_TemplateWithTheScopeInItsFileName_IsNotRead()
    {
        // Arrange
        folder.Write("TestEvent.en.acme.subject.txt", "The name before D199", _firstWriteUtc);

        // Act
        var result = await Store.FindAsync(ALookup(scope: "acme"), Ct);

        // Assert
        Assert.Equal("Acme order {{OrderNumber}}", result.Value?.Text);
    }

    [Fact]
    public async Task FindAsync_ScopeThatLeadsOutOfTheRootFolder_ReadsNoFileOutsideTheRootFolder()
    {
        // Arrange
        folder.Write("secret.subject.txt", "Secret", _firstWriteUtc, folder.Parent);

        // Act
        var result = await Store.FindAsync(ALookup(scope: "x/../../secret"), Ct);

        // Assert
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task FindAsync_PartThatCannotBeRead_GivesUnavailable()
    {
        // Arrange
        Directory.CreateDirectory(Path.Combine(folder.Root, "Unreadable.en.subject.txt"));

        // Act
        var result = await Store.FindAsync(ALookup() with { Type = new("Unreadable") }, Ct);

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
