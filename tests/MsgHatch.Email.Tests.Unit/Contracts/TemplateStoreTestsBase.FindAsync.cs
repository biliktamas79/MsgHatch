using MsgHatch.Email.Templates;

namespace MsgHatch.Email.Tests.Unit.Contracts;

public abstract partial class TemplateStoreTestsBase<TStore>
{
    [Theory]
    [InlineData("subject", TemplatePartKind.PlainText, "Order {{OrderNumber}} shipped")]
    [InlineData("body", TemplatePartKind.Html, "<p>Hello {{Name}}</p>")]
    [InlineData("body", TemplatePartKind.PlainText, "Hello {{Name}}")]
    public async Task FindAsync_PartOfAnExistingTemplate_GivesTheTextOfItsFile(string part, TemplatePartKind kind, string expectedText)
    {
        // Act
        var result = await Store.FindAsync(ALookup(part: part, kind: kind), Ct);

        // Assert
        Assert.Equal(expectedText, result.Value?.Text);
    }

    [Fact]
    public async Task FindAsync_MissingTemplate_GivesNull()
    {
        // Act
        var result = await Store.FindAsync(ALookup(language: "de"), Ct);

        // Assert
        Assert.Null(result.Value);
    }

    [Theory]
    [InlineData("subject", TemplatePartKind.PlainText, "Acme order {{OrderNumber}}")]
    [InlineData("body", TemplatePartKind.Html, "<p>Acme {{Name}}</p>")]
    public async Task FindAsync_PartOfATemplateWithAScope_GivesTheTextOfTheScope(string part, TemplatePartKind kind, string expectedText)
    {
        // Act
        var result = await Store.FindAsync(ALookup(scope: "acme", part: part, kind: kind), Ct);

        // Assert
        Assert.Equal(expectedText, result.Value?.Text);
    }

    [Theory]
    [MemberData(nameof(TemplateStoreTestRows.ScopeFolderRows), MemberType = typeof(TemplateStoreTestRows))]
    public async Task FindAsync_ScopeFolder_GivesTheSubjectOfTheFolderOfTheScopeOrNull(string row, string scope, string? expectedSubject)
    {
        // Act
        var result = await Store.FindAsync(ALookup(scope: scope), Ct);

        // Assert
        Assert.Equal((row, expectedSubject), (row, result.Value?.Text));
    }

    [Fact]
    public async Task FindAsync_ScopeWithNoTemplateOfItsOwn_GivesNullSoThatTheSelectorFallsBackToNoScope()
    {
        // Act
        var result = await Store.FindAsync(ALookup(scope: "other"), Ct);

        // Assert
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task FindAsync_ScopeFolderWithOnlyASubject_GivesNullForTheBodySoThatTheSelectorFallsBackToNoScope()
    {
        // Act
        var result = await Store.FindAsync(ALookup(scope: "42", part: "body", kind: TemplatePartKind.Html), Ct);

        // Assert
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task FindAsync_OneTemplateTwoTimes_GivesTheSameSourceInstance()
    {
        // Arrange
        var first = await Store.FindAsync(ALookup(), Ct);

        // Act
        var second = await Store.FindAsync(ALookup(), Ct);

        // Assert
        Assert.Same(first.Value, second.Value);
    }

    [Theory]
    [InlineData("subject", TemplatePartKind.PlainText, "Ödön Űr rendelése: ő ű \"\\ <&>")]
    [InlineData("body", TemplatePartKind.Html, "<p>Kedves {{Name}}, ő ű</p>")]
    [InlineData("body", TemplatePartKind.PlainText, "Kedves {{Name}}, ő ű")]
    public async Task FindAsync_TemplateWithAccentedAndSpecialCharacters_GivesTheTextUnchanged(string part, TemplatePartKind kind, string expectedText)
    {
        // Act
        var result = await Store.FindAsync(ALookup(language: "hu", part: part, kind: kind), Ct);

        // Assert
        Assert.Equal(expectedText, result.Value?.Text);
    }

    [Fact]
    public async Task FindAsync_TemplateWithOnlyASubject_GivesTheSubjectAndNullForTheBody()
    {
        // Arrange
        var subject = ALookup(part: "subject") with { Type = new("OnlySubject") };
        var body = ALookup(part: "body", kind: TemplatePartKind.Html) with { Type = new("OnlySubject") };

        // Act
        var subjectResult = await Store.FindAsync(subject, Ct);
        var bodyResult = await Store.FindAsync(body, Ct);

        // Assert
        Assert.Equal(("Only a subject", (TemplateSource?)null), (subjectResult.Value?.Text, bodyResult.Value));
    }
}
