using System.Reflection;
using Microsoft.AspNetCore.Components;
using MsgHatch.Subscriptions.AdminUI.Tests.Unit.TestTypes;

namespace MsgHatch.Subscriptions.AdminUI.Tests.Unit;

public sealed class SubscriptionAdminRoutesUnitTests
{
    [Fact]
    public void Edit_Id_GivesTheAddressOfTheEditPage()
    {
        Assert.Equal("/msghatch/subscriptions/3f2c6b1e-7a40-4c1d-9a7e-1b2c3d4e5f60", SubscriptionAdminRoutes.Edit(SubscriptionTestData.Id));
    }

    [Fact]
    public void Pages_RouteTemplates_AreTheAddressesOfTheRouteBuilders()
    {
        Assert.Equal([SubscriptionAdminRoutes.List], TemplatesOf<SubscriptionListPage>());
        Assert.Equal([SubscriptionAdminRoutes.MessageTypes], TemplatesOf<MessageTypesPage>());
        Assert.Equal([SubscriptionAdminRoutes.Verification], TemplatesOf<RoutingVerificationPage>());
        Assert.Equal([SubscriptionAdminRoutes.New, SubscriptionAdminRoutes.List + "/{Id:guid}"], TemplatesOf<SubscriptionEditPage>());
    }

    [Fact]
    public void ComponentTypes_OfTheLibrary_SetNoRenderMode()
    {
        var withRenderMode = typeof(SubscriptionAdminRoutes).Assembly.GetTypes()
            .Where(static type => typeof(IComponent).IsAssignableFrom(type) && type.GetCustomAttribute<RenderModeAttribute>(inherit: true) is not null);

        Assert.Empty(withRenderMode);
    }

    private static string[] TemplatesOf<TPage>() => [.. typeof(TPage).GetCustomAttributes<RouteAttribute>().Select(static route => route.Template).Order(StringComparer.Ordinal)];
}
