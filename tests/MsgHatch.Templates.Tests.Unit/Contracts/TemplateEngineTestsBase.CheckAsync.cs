using MsgHatch.Templates;

namespace MsgHatch.Templates.Tests.Unit.Contracts;

public abstract partial class TemplateEngineTestsBase<TEngine>
{
    [Theory]
    [InlineData(TemplatePartKind.PlainText)]
    [InlineData(TemplatePartKind.Html)]
    public async Task CheckAsync_ValidText_GivesNoIssue(TemplatePartKind kind)
    {
        // Act
        var issues = await Engine.CheckAsync(ALookup(kind), TestData.InEngineSyntax("Order of {Name}: {Count}"), Ct);

        // Assert
        Assert.Empty(issues.Value);
    }

    [Fact]
    public async Task CheckAsync_TextWithAnUnclosedTag_GivesOneIssue()
    {
        // Act
        var issues = await Engine.CheckAsync(ALookup(), "Order of {{ Name", Ct);

        // Assert
        Assert.Single(issues.Value);
    }

    protected TemplateLookup ALookup(TemplatePartKind kind = TemplatePartKind.PlainText) => new(Type, "en", Scope: null, Part, kind);
}
