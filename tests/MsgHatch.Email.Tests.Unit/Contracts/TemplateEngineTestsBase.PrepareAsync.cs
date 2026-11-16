using MsgHatch.Email.Templates;

namespace MsgHatch.Email.Tests.Unit.Contracts;

public abstract partial class TemplateEngineTestsBase<TEngine>
{
    [Fact]
    public async Task PrepareAsync_MissingTemplate_GivesNotFound()
    {
        // Act
        var result = await Engine.PrepareAsync(ARequest(), Ct);

        // Assert
        Assert.Equal(404, CodeOf(result));
    }

    [Fact]
    public async Task PrepareAsync_TemplateOfAnotherKind_GivesNotFound()
    {
        // Arrange
        TestData.Seed(Type, "en", Part, TemplatePartKind.Html, "<p>{Name}</p>");

        // Act
        var result = await Engine.PrepareAsync(ARequest(kind: TemplatePartKind.PlainText), Ct);

        // Assert
        Assert.Equal(404, CodeOf(result));
    }

    [Fact]
    public async Task PrepareAsync_ExistingTemplate_GivesATemplate()
    {
        // Arrange
        TestData.Seed(Type, "en", Part, TemplatePartKind.PlainText, "Order {Name}");

        // Act
        var result = await Engine.PrepareAsync(ARequest(), Ct);

        // Assert
        Assert.NotNull(result.Value);
    }
}
