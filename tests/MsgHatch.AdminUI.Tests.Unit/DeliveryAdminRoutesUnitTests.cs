using System.Reflection;
using Microsoft.AspNetCore.Components;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Routing;

namespace MsgHatch.AdminUI.Tests.Unit;

public sealed class DeliveryAdminRoutesUnitTests
{
    [Fact]
    public void OutgoingMessage_Id_GivesTheAddressOfTheMessagePage()
    {
        Assert.Equal(
            "/msghatch/outbox/00000000-0000-0000-0000-000000000001",
            DeliveryAdminRoutes.OutgoingMessage(Guid.Parse("00000000-0000-0000-0000-000000000001")));
    }

    [Fact]
    public void ReceivedMessage_Id_GivesTheAddressOfTheMessagePage()
    {
        Assert.Equal(
            "/msghatch/inbox/00000000-0000-0000-0000-000000000001",
            DeliveryAdminRoutes.ReceivedMessage(Guid.Parse("00000000-0000-0000-0000-000000000001")));
    }

    [Fact]
    public void OutboxOf_NoFilter_GivesTheAddressOfTheList()
    {
        Assert.Equal("/msghatch/outbox", DeliveryAdminRoutes.OutboxOf());
    }

    [Fact]
    public void OutboxOf_EachFilter_GivesEachValueEscapedInTheQuery()
    {
        var address = DeliveryAdminRoutes.OutboxOf(
            DeliveryStatus.Failed, new MessageType("Requests"), new ChannelId("Email"), new DeliveryTarget(DeliveryTargetKind.Company, "a b&c"),
            Guid.Parse("00000000-0000-0000-0000-0000000000aa"), new DateTime(2030, 1, 1, 10, 0, 5, DateTimeKind.Utc),
            new DateTime(2030, 1, 2, 0, 0, 0, DateTimeKind.Utc), includeTest: true);

        Assert.Equal(
            "/msghatch/outbox?status=Failed&type=Requests&channel=Email&targetKind=Company&targetId=a%20b%26c"
            + "&source=00000000-0000-0000-0000-0000000000aa&from=2030-01-01T10%3A00%3A05&to=2030-01-02T00%3A00%3A00&test=true",
            address);
    }

    [Fact]
    public void InboxOf_NoFilter_GivesTheAddressOfTheList()
    {
        Assert.Equal("/msghatch/inbox", DeliveryAdminRoutes.InboxOf());
    }

    [Fact]
    public void InboxOf_EachFilter_GivesEachValueEscapedInTheQuery()
    {
        var address = DeliveryAdminRoutes.InboxOf(
            DeliveryStatus.Pending, new MessageType("Requests"), new DateTime(2030, 1, 1, 10, 0, 5, DateTimeKind.Utc),
            new DateTime(2030, 1, 2, 0, 0, 0, DateTimeKind.Utc), includeTest: true);

        Assert.Equal("/msghatch/inbox?status=Pending&type=Requests&from=2030-01-01T10%3A00%3A05&to=2030-01-02T00%3A00%3A00&test=true", address);
    }

    [Fact]
    public void Pages_RouteTemplates_AreTheAddressesOfTheRouteBuilders()
    {
        Assert.Equal([DeliveryAdminRoutes.Outbox], TemplatesOf<OutboxListPage>());
        Assert.Equal([DeliveryAdminRoutes.Outbox + "/{Id:guid}"], TemplatesOf<OutgoingMessagePage>());
        Assert.Equal([DeliveryAdminRoutes.Inbox], TemplatesOf<InboxListPage>());
        Assert.Equal([DeliveryAdminRoutes.Inbox + "/{Id:guid}"], TemplatesOf<ReceivedMessagePage>());
    }

    private static string[] TemplatesOf<TPage>() => [.. typeof(TPage).GetCustomAttributes<RouteAttribute>().Select(static route => route.Template).Order(StringComparer.Ordinal)];
}
