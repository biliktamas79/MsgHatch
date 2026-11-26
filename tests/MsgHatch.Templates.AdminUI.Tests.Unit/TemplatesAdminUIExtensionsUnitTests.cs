using System.Reflection;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Templates.AdminUI.DependencyInjection;
using MsgHatch.Templates.AdminUI.Tests.Unit.TestTypes;

namespace MsgHatch.Templates.AdminUI.Tests.Unit;

public sealed class TemplatesAdminUIExtensionsUnitTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void AddTemplateEditor_NoEngineName_Throws(string? engine)
    {
        // Act and assert
        Assert.ThrowsAny<ArgumentException>(() => new ServiceCollection().AddTemplateEditor<TestTemplateEditor>(engine!));
    }

    [Fact]
    public void ComponentTypes_OfTheLibrary_SetNoRenderMode()
    {
        // Act
        var withRenderMode = typeof(TemplateAdminRoutes).Assembly.GetTypes()
            .Where(static type => typeof(IComponent).IsAssignableFrom(type) && type.GetCustomAttribute<RenderModeAttribute>(inherit: true) is not null);

        // Assert
        Assert.Empty(withRenderMode);
    }
}
