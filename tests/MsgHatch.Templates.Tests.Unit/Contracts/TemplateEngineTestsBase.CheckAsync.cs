using MsgHatch.Templates;

namespace MsgHatch.Templates.Tests.Unit.Contracts;

public abstract partial class TemplateEngineTestsBase<TEngine>
{
    [Theory]
    [InlineData(TemplatePartKind.PlainText)]
    [InlineData(TemplatePartKind.Html)]
    public async Task CheckAsync_ValidText_GivesNoError(TemplatePartKind kind)
    {
        // Act
        var error = await Engine.CheckAsync(ALookup(kind), TestData.InEngineSyntax("Order of {Name}: {Count}"), Ct);

        // Assert
        Assert.Null(error);
    }

    [Fact]
    public async Task CheckAsync_TextWithAnUnclosedTag_GivesTemplateInvalid()
    {
        // Act
        var error = await Engine.CheckAsync(ALookup(), "Order of {{ Name", Ct);

        // Assert
        Assert.Equal(422, error?.Type.Code);
    }

    protected TemplateLookup ALookup(TemplatePartKind kind = TemplatePartKind.PlainText) => new(Type, "en", Scope: null, Part, kind);
}
