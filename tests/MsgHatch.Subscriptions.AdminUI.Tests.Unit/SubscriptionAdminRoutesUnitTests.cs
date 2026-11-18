using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.AspNetCore.Components;
using MsgHatch.Subscriptions.AdminUI.DependencyInjection;
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
        Assert.Equal([SubscriptionAdminRoutes.Verification], TemplatesOf<RoutingVerificationPage>());
        Assert.Equal([SubscriptionAdminRoutes.List + "/{Id:guid}"], TemplatesOf<SubscriptionEditPage>());
    }

    [Fact]
    public void New_List_GivesTheListAddressWithTheQueryThatOpensTheDialog()
    {
        Assert.Equal("/msghatch/subscriptions?new=true", SubscriptionAdminRoutes.New);
    }

    [Theory]
    [InlineData(SubscriptionLevel.Default, "/msghatch/subscriptions?type=Requests&level=Default")]
    [InlineData(SubscriptionLevel.User, "/msghatch/subscriptions?type=Requests&level=User")]
    public void ListOf_MessageTypeAndLevel_GivesTheListAddressWithTheQueryOfBothFilters(SubscriptionLevel level, string expected)
    {
        Assert.Equal(expected, SubscriptionAdminRoutes.ListOf(SubscriptionTestData.Requests, level));
    }

    [Fact]
    public void ComponentTypes_OfTheLibrary_SetNoRenderMode()
    {
        var withRenderMode = typeof(SubscriptionAdminRoutes).Assembly.GetTypes()
            .Where(static type => typeof(IComponent).IsAssignableFrom(type) && type.GetCustomAttribute<RenderModeAttribute>(inherit: true) is not null);

        Assert.Empty(withRenderMode);
    }

    [Fact]
    public void AddMsgHatchSubscriptionsAdminUI_DynamicDependencies_KeepEachPageOfTheLibrary()
    {
        var pages = typeof(SubscriptionAdminRoutes).Assembly.GetTypes()
            .Where(static type => type.GetCustomAttributes<RouteAttribute>().Any())
            .Select(static type => type.Name);
        var kept = typeof(SubscriptionsAdminUIExtensions).GetMethod(nameof(SubscriptionsAdminUIExtensions.AddMsgHatchSubscriptionsAdminUI))!
            .GetCustomAttributes<DynamicDependencyAttribute>()
            .Select(static dependency => dependency.Type!.Name);

        Assert.Equal(pages.Order(StringComparer.Ordinal), kept.Order(StringComparer.Ordinal));
    }

    private static string[] TemplatesOf<TPage>() => [.. typeof(TPage).GetCustomAttributes<RouteAttribute>().Select(static route => route.Template).Order(StringComparer.Ordinal)];
}
