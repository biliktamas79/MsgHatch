using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.AspNetCore.Components;
using MsgHatch.AdminUI.DependencyInjection;
using MsgHatch.Domain.Identity;

namespace MsgHatch.AdminUI.Tests.Unit;

public sealed class RegistryAdminRoutesUnitTests
{
    [Fact]
    public void Channel_Id_GivesTheAddressOfTheViewPage()
    {
        Assert.Equal("/msghatch/channels/Email", RegistryAdminRoutes.Channel(new ChannelId("Email")));
    }

    [Fact]
    public void Pages_RouteTemplates_AreTheAddressesOfTheRouteBuilders()
    {
        Assert.Equal([RegistryAdminRoutes.Channels], TemplatesOf<ChannelListPage>());
        Assert.Equal([RegistryAdminRoutes.Channels + "/{ChannelName}"], TemplatesOf<ChannelViewPage>());
        Assert.Equal(["/msghatch/message-types"], TemplatesOf<MessageTypeListPage>());
    }

    [Fact]
    public void AddMsgHatchAdminUI_DynamicDependencies_KeepEachPageOfTheLibrary()
    {
        var pages = typeof(RegistryAdminRoutes).Assembly.GetTypes()
            .Where(static type => type.GetCustomAttributes<RouteAttribute>().Any())
            .Select(static type => type.Name);
        var kept = typeof(AdminUIExtensions).GetMethod(nameof(AdminUIExtensions.AddMsgHatchAdminUI))!
            .GetCustomAttributes<DynamicDependencyAttribute>()
            .Select(static dependency => dependency.Type!.Name);

        Assert.Equal(pages.Order(StringComparer.Ordinal), kept.Order(StringComparer.Ordinal));
    }

    private static string[] TemplatesOf<TPage>() => [.. typeof(TPage).GetCustomAttributes<RouteAttribute>().Select(static route => route.Template).Order(StringComparer.Ordinal)];
}
