using System.Reflection;
using Microsoft.AspNetCore.Components;

namespace MsgHatch.AdminUI.Tests.Unit;

public sealed class RenderModeUnitTests
{
    [Fact]
    public void ComponentTypes_OfTheLibrary_SetNoRenderMode()
    {
        var withRenderMode = typeof(AdminErrors).Assembly.GetTypes()
            .Where(static type => typeof(IComponent).IsAssignableFrom(type) && type.GetCustomAttribute<RenderModeAttribute>(inherit: true) is not null);

        Assert.Empty(withRenderMode);
    }
}
