using MsgHatch.Email.Templates;

namespace MsgHatch.Email.Tests.Unit.Contracts;

public abstract partial class TemplateStoreTestsBase<TStore>
{
    [Fact]
    public async Task FindAsync_ExistingTemplate_GivesItsSubjectHtmlAndTextParts()
    {
        // Arrange
        var lookup = new TemplateLookup(TestEvent, "en", Scope: null);

        // Act
        var result = await Store.FindAsync(lookup, Ct);

        // Assert
        Assert.Equal(
            [
                ("subject", TemplatePartKind.PlainText, "Order {{OrderNumber}} shipped"),
                ("html", TemplatePartKind.Html, "<p>Hello {{Name}}</p>"),
                ("text", TemplatePartKind.PlainText, "Hello {{Name}}"),
            ],
            PartsOf(result.Value));
    }

    [Fact]
    public async Task FindAsync_MissingTemplate_GivesNull()
    {
        // Arrange
        var lookup = new TemplateLookup(TestEvent, "de", Scope: null);

        // Act
        var result = await Store.FindAsync(lookup, Ct);

        // Assert
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task FindAsync_TemplateWithAScope_GivesTheTemplateOfTheScope()
    {
        // Arrange
        var lookup = new TemplateLookup(TestEvent, "en", "acme");

        // Act
        var result = await Store.FindAsync(lookup, Ct);

        // Assert
        Assert.Equal(
            [("subject", TemplatePartKind.PlainText, "Acme order {{OrderNumber}}"), ("html", TemplatePartKind.Html, "<p>Acme {{Name}}</p>")],
            PartsOf(result.Value));
    }

    [Fact]
    public async Task FindAsync_ScopeWithNoTemplateOfItsOwn_GivesNullSoThatTheSelectorFallsBackToNoScope()
    {
        // Arrange
        var lookup = new TemplateLookup(TestEvent, "en", "other");

        // Act
        var result = await Store.FindAsync(lookup, Ct);

        // Assert
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task FindAsync_OneTemplateTwoTimes_GivesTheSameSourceInstance()
    {
        // Arrange
        var lookup = new TemplateLookup(TestEvent, "en", Scope: null);
        var first = await Store.FindAsync(lookup, Ct);

        // Act
        var second = await Store.FindAsync(lookup, Ct);

        // Assert
        Assert.Same(first.Value, second.Value);
    }

    [Fact]
    public async Task FindAsync_TemplateWithAccentedAndSpecialCharacters_GivesTheTextUnchanged()
    {
        // Arrange
        var lookup = new TemplateLookup(TestEvent, "hu", Scope: null);

        // Act
        var result = await Store.FindAsync(lookup, Ct);

        // Assert
        Assert.Equal(
            [
                ("subject", TemplatePartKind.PlainText, "Ödön Űr rendelése: ő ű \"\\ <&>"),
                ("html", TemplatePartKind.Html, "<p>Kedves {{Name}}, ő ű</p>"),
                ("text", TemplatePartKind.PlainText, "Kedves {{Name}}, ő ű"),
            ],
            PartsOf(result.Value));
    }

    [Fact]
    public async Task FindAsync_TemplateWithOnlySomeParts_GivesTheExistingParts()
    {
        // Arrange
        var lookup = new TemplateLookup(new("OnlySubject"), "en", Scope: null);

        // Act
        var result = await Store.FindAsync(lookup, Ct);

        // Assert
        Assert.Equal([("subject", TemplatePartKind.PlainText, "Only a subject")], PartsOf(result.Value));
    }
}
