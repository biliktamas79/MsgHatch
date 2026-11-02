using MsgHatch.Email.Engines;

namespace MsgHatch.Email.Tests.Unit.Contracts;

public abstract partial class EmailTemplateEngineTestsBase<TEngine>
{
    [Fact]
    public async Task PrepareAsync_MissingTemplate_GivesNotFound()
    {
        // Arrange
        var request = new EmailTemplateRequest(Type, "en", Scope: null);

        // Act
        var result = await Engine.PrepareAsync(request, Ct);

        // Assert
        Assert.Equal(404, CodeOf(result));
    }

    [Fact]
    public async Task PrepareAsync_TemplateWithAnUnknownPlaceholderName_GivesTemplateInvalid()
    {
        // Arrange
        TestData.Seed(Type, "en", "Order {Unknown}", "<p>{Name}</p>", "{Name}");

        // Act
        var result = await Engine.PrepareAsync(new EmailTemplateRequest(Type, "en", Scope: null), Ct);

        // Assert
        Assert.Equal(422, CodeOf(result));
    }

    [Fact]
    public async Task PrepareAsync_ExistingTemplate_GivesATemplate()
    {
        // Arrange
        TestData.Seed(Type, "en", "Order", "<p>{Name}</p>", null);

        // Act
        var result = await Engine.PrepareAsync(new EmailTemplateRequest(Type, "en", Scope: null), Ct);

        // Assert
        Assert.NotNull(result.Value);
    }
}
