using MsgHatch.Domain.Identity;
using MsgHatch.Templates;

namespace MsgHatch.Templates.Tests.Unit.Contracts;

/// <summary>
/// The use cases of <see cref="ITemplateWriter"/> (section 5.5, D218, D223). Each writable store gets them with one
/// subclass that builds the store. Each test method uses a message type of its own, with the name of the method, so the
/// tests share no template.
/// </summary>
/// <typeparam name="TStore">The store type.</typeparam>
public abstract class TemplateWriterTestsBase<TStore>(TStore store)
    where TStore : class, ITemplateStore, ITemplateWriter
{
    protected TStore Store { get; } = store;

    protected MessageType Type { get; } = new(TestContext.Current.TestMethod!.MethodName);

    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SaveAsync_NewTemplateWithVersion0_GivesTheEntryOfTheTextThatFindAsyncReads()
    {
        // Arrange
        const string text = "Order {{Name}}";

        // Act
        var entry = await Store.SaveAsync(ALookup(), text, 0, Ct);

        // Assert
        var source = await Store.FindAsync(ALookup(), Ct);
        Assert.Equal(
            (ALookup(), true, text, entry.Value.Version),
            (entry.Value.Lookup, entry.Value.Version > 0, source.Value?.Text, source.Value?.Key.Version));
    }

    [Fact]
    public async Task SaveAsync_ExistingTemplateWithItsVersion_GivesALaterVersionWithTheNewText()
    {
        // Arrange
        const string secondText = "Second";
        var first = await Store.SaveAsync(ALookup(), "First", 0, Ct);

        // Act
        var second = await Store.SaveAsync(ALookup(), secondText, first.Value.Version, Ct);

        // Assert
        var source = await Store.FindAsync(ALookup(), Ct);
        Assert.Equal(
            (true, secondText, second.Value.Version),
            (second.Value.Version > first.Value.Version, source.Value?.Text, source.Value?.Key.Version));
    }

    [Fact]
    public async Task SaveAsync_TwentySavesInARow_GiveTwentyVersionsInOrder()
    {
        // Act
        var versions = await SaveInARowAsync(20);

        // Assert
        Assert.Equal([.. versions.Distinct().Order()], versions);
    }

    [Fact]
    public async Task SaveAsync_OldVersion_GivesConflictAndKeepsTheText()
    {
        // Arrange
        const string storedText = "Second";
        var first = await Store.SaveAsync(ALookup(), "First", 0, Ct);
        await Store.SaveAsync(ALookup(), storedText, first.Value.Version, Ct);

        // Act
        var result = await Store.SaveAsync(ALookup(), "Third", first.Value.Version, Ct);

        // Assert
        var source = await Store.FindAsync(ALookup(), Ct);
        Assert.Equal((409, storedText), (result.Error?.Type.Code, source.Value?.Text));
    }

    [Fact]
    public async Task SaveAsync_Version0OfAnExistingTemplate_GivesConflictAndKeepsTheText()
    {
        // Arrange
        const string storedText = "First";
        await Store.SaveAsync(ALookup(), storedText, 0, Ct);

        // Act
        var result = await Store.SaveAsync(ALookup(), "Second", 0, Ct);

        // Assert
        var source = await Store.FindAsync(ALookup(), Ct);
        Assert.Equal((409, storedText), (result.Error?.Type.Code, source.Value?.Text));
    }

    [Fact]
    public async Task SaveAsync_VersionOfAMissingTemplate_GivesConflictAndMakesNoTemplate()
    {
        // Act
        var result = await Store.SaveAsync(ALookup(), "First", 638_000_000_000_000_000, Ct);

        // Assert
        var source = await Store.FindAsync(ALookup(), Ct);
        Assert.Equal((409, null), (result.Error?.Type.Code, source.Value?.Text));
    }

    [Fact]
    public async Task SaveAsync_TenParallelSavesWithOneVersion_GiveOneEntryAndNineConflicts()
    {
        // Act
        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(save => Task.Run(() => Store.SaveAsync(ALookup(), $"Text {save}", 0, Ct).AsTask(), Ct)));

        // Assert
        Assert.Equal((1, 9), (results.Count(static result => !result.IsError), results.Count(static result => result.Error?.Type.Code == 409)));
    }

    [Fact]
    public async Task SaveAsync_TwoKindsOfOnePart_GiveTwoTemplates()
    {
        // Arrange
        const string htmlText = "<p>Html</p>";
        const string plainText = "Text";

        // Act
        await Store.SaveAsync(ALookup(kind: TemplatePartKind.Html), htmlText, 0, Ct);
        await Store.SaveAsync(ALookup(kind: TemplatePartKind.PlainText), plainText, 0, Ct);

        // Assert
        var html = await Store.FindAsync(ALookup(kind: TemplatePartKind.Html), Ct);
        var text = await Store.FindAsync(ALookup(kind: TemplatePartKind.PlainText), Ct);
        Assert.Equal((htmlText, plainText), (html.Value?.Text, text.Value?.Text));
    }

    [Fact]
    public async Task SaveAsync_TemplateOfAScope_IsFoundOnlyWithThatScope()
    {
        // Arrange
        const string scope = "42";
        const string companyText = "Company 42";

        // Act
        await Store.SaveAsync(ALookup(scope: scope), companyText, 0, Ct);

        // Assert
        var scoped = await Store.FindAsync(ALookup(scope: scope), Ct);
        var unscoped = await Store.FindAsync(ALookup(), Ct);
        var other = await Store.FindAsync(ALookup(scope: "43"), Ct);
        Assert.Equal((companyText, null, null), (scoped.Value?.Text, unscoped.Value?.Text, other.Value?.Text));
    }

    [Fact]
    public async Task DeleteAsync_ExistingTemplate_RemovesIt()
    {
        // Arrange
        await Store.SaveAsync(ALookup(), "First", 0, Ct);

        // Act
        var error = await Store.DeleteAsync(ALookup(), Ct);

        // Assert
        var source = await Store.FindAsync(ALookup(), Ct);
        Assert.Equal((null, null), (error, source.Value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("missing-company")]
    public async Task DeleteAsync_MissingTemplate_GivesNoError(string? scope)
    {
        // Act
        var error = await Store.DeleteAsync(ALookup(scope: scope), Ct);

        // Assert
        Assert.Null(error);
    }

    [Fact]
    public async Task ListAsync_TypeOfSavedTemplates_GivesTheirEntriesInTheOrderOfScopeLanguagePartAndKind()
    {
        // Arrange
        TemplateEntry[] saved =
        [
            (await Store.SaveAsync(ALookup("hu", scope: "42", part: "body", kind: TemplatePartKind.Html), "Text", 0, Ct)).Value,
            (await Store.SaveAsync(ALookup("hu", part: "subject"), "Text", 0, Ct)).Value,
            (await Store.SaveAsync(ALookup("en", part: "body", kind: TemplatePartKind.PlainText), "Text", 0, Ct)).Value,
            (await Store.SaveAsync(ALookup("en", part: "body", kind: TemplatePartKind.Html), "Text", 0, Ct)).Value,
        ];

        // Act
        var entries = await Store.ListAsync(new TemplateQuery(Type), Ct);

        // Assert
        Assert.Equal([saved[2], saved[3], saved[1], saved[0]], entries.Value);
    }

    [Fact]
    public async Task ListAsync_Scope_GivesOnlyTheTemplatesOfThatScope()
    {
        // Arrange
        var company = await Store.SaveAsync(ALookup(scope: "acme"), "Acme", 0, Ct);
        await Store.SaveAsync(ALookup(scope: "other"), "Other", 0, Ct);
        await Store.SaveAsync(ALookup(), "No company", 0, Ct);

        // Act
        var entries = await Store.ListAsync(new TemplateQuery(Type, "acme"), Ct);

        // Assert
        Assert.Equal([company.Value], entries.Value);
    }

    [Fact]
    public async Task ListAsync_DeletedTemplate_HasNoEntry()
    {
        // Arrange
        var kept = await Store.SaveAsync(ALookup("en"), "Kept", 0, Ct);
        await Store.SaveAsync(ALookup("hu"), "Deleted", 0, Ct);
        await Store.DeleteAsync(ALookup("hu"), Ct);

        // Act
        var entries = await Store.ListAsync(new TemplateQuery(Type), Ct);

        // Assert
        Assert.Equal([kept.Value], entries.Value);
    }

    [Fact]
    public async Task ListAsync_TypeWithNoTemplate_GivesNoEntry()
    {
        // Act
        var entries = await Store.ListAsync(new TemplateQuery(Type), Ct);

        // Assert
        Assert.Empty(entries.Value);
    }

    protected TemplateLookup ALookup(
        string language = "en", string? scope = null, string part = "subject", TemplatePartKind kind = TemplatePartKind.PlainText) =>
        new(Type, language, scope, part, kind);

    private async Task<List<long>> SaveInARowAsync(int saves)
    {
        var versions = new List<long>();
        for (var save = 0; save < saves; save++)
        {
            var entry = await Store.SaveAsync(ALookup(), $"Text {save}", versions.LastOrDefault(), Ct);
            versions.Add(entry.Value.Version);
        }

        return versions;
    }
}
