using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using MsgHatch.AdminUI.DependencyInjection;
using MsgHatch.Domain.Identity;

namespace MsgHatch.AdminUI.Tests.Unit;

public sealed class RegistryAdminRoutesUnitTests
{
    [Fact]
    public void Channel_Id_GivesTheAddressOfTheViewPage()
    {
        // Act and assert
        Assert.Equal("/msghatch/channels/Email", RegistryAdminRoutes.Channel(new ChannelId("Email")));
    }

    [Fact]
    public void ChannelEdit_Id_GivesTheAddressOfTheEditPage()
    {
        // Act and assert
        Assert.Equal("/msghatch/channels/Email/edit", RegistryAdminRoutes.ChannelEdit(new ChannelId("Email")));
    }

    [Fact]
    public void MessageTypeEdit_Type_GivesTheAddressOfTheEditPage()
    {
        // Act and assert
        Assert.Equal("/msghatch/message-types/Invoices/edit", RegistryAdminRoutes.MessageTypeEdit(new MessageType("Invoices")));
    }

    [Fact]
    public void Pages_RouteTemplates_AreTheAddressesOfTheRouteBuilders()
    {
        // Act and assert
        Assert.Equal([RegistryAdminRoutes.Channels], TemplatesOf<ChannelListPage>());
        Assert.Equal([RegistryAdminRoutes.Channels + "/{ChannelName}"], TemplatesOf<ChannelViewPage>());
        Assert.Equal([RegistryAdminRoutes.Channels + "/{ChannelName}/edit"], TemplatesOf<ChannelEditPage>());
        Assert.Equal(["/msghatch/message-types"], TemplatesOf<MessageTypeListPage>());
        Assert.Equal([RegistryAdminRoutes.MessageTypes + "/{TypeName}/edit"], TemplatesOf<MessageTypeEditPage>());
    }

    [Fact]
    public void AddMsgHatchAdminUI_DynamicDependencies_KeepEachPageOfTheLibrary()
    {
        // Act
        var pages = typeof(RegistryAdminRoutes).Assembly.GetTypes()
            .Where(static type => type.GetCustomAttributes<RouteAttribute>().Any())
            .Select(static type => type.Name);
        var kept = typeof(AdminUIExtensions).GetMethod(nameof(AdminUIExtensions.AddMsgHatchAdminUI))!
            .GetCustomAttributes<DynamicDependencyAttribute>()
            .Select(static dependency => dependency.Type!.Name);

        // Assert
        Assert.Equal(pages.Order(StringComparer.Ordinal), kept.Order(StringComparer.Ordinal));
    }

    private static string[] TemplatesOf<TPage>() => [.. typeof(TPage).GetCustomAttributes<RouteAttribute>().Select(static route => route.Template).Order(StringComparer.Ordinal)];

    [Fact]
    public void AddMsgHatchAdminUI_HostWithNoClock_AddsTheSystemClock()
    {
        // Act
        using var provider = new ServiceCollection().AddMsgHatchAdminUI().BuildServiceProvider();

        // Assert
        Assert.Same(TimeProvider.System, provider.GetRequiredService<TimeProvider>());
    }

    [Fact]
    public void AddMsgHatchAdminUI_HostWithAClock_KeepsTheClockOfTheHost()
    {
        // Arrange
        var clock = new FakeTimeProvider();

        // Act
        using var provider = new ServiceCollection().AddSingleton<TimeProvider>(clock).AddMsgHatchAdminUI().BuildServiceProvider();

        // Assert
        Assert.Same(clock, provider.GetRequiredService<TimeProvider>());
    }
}
