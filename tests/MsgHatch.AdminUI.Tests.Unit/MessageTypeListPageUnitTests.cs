using Bunit;
using MsgHatch.AdminUI.Tests.Unit.TestTypes;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Management;
using NSubstitute;

namespace MsgHatch.AdminUI.Tests.Unit;

public sealed class MessageTypeListPageUnitTests : RegistryPageContext
{
    [Fact]
    public void Render_MessageTypes_ShowsEachRowWithItsDisplayNameItsDescriptionItsEnabledFlagAndItsChannels()
    {
        Queries.GetMessageTypesAsync(Arg.Any<CancellationToken>()).Returns(Ok<IReadOnlyList<RegistryMessageType>>(
            [MessageTypes[0] with { Description = "Sent when a client asks" }, MessageTypes[1]]));

        var cut = Render<MessageTypeListPage>();

        var rows = cut.FindAll("tr[data-row]");
        Assert.Equal(["Requests", "Requests of a client", "Sent when a client asks", "Yes", "Email Sms"], rows[0].Children.Select(TextOf));
        Assert.Equal(["Notices", "", "", "No", "Email"], rows[1].Children.Select(TextOf));
        Assert.Equal("Message types", cut.Find("[data-list-header] h1").TextContent);
    }

    [Fact]
    public void Render_ChannelsOfAMessageType_LinkToTheirViewPages()
    {
        var cut = Render<MessageTypeListPage>();

        Assert.Equal(
            [RegistryAdminRoutes.Channel(Email).TrimStart('/'), RegistryAdminRoutes.Channel(Sms).TrimStart('/')],
            cut.FindAll("tr[data-row]")[0].QuerySelectorAll("[data-channel] a").Select(static link => link.GetAttribute("href")));
    }

    [Fact]
    public void Render_DisabledChannelOfAMessageType_HasTheMarkDisabled()
    {
        var cut = Render<MessageTypeListPage>();

        Assert.Equal(
            ("disabled", 1),
            (cut.Find("tr[data-row]:nth-child(3) [data-channel=Email] [data-disabled]").TextContent, cut.FindAll("[data-disabled]").Count));
    }

    [Fact]
    public void Render_NoMessageType_ShowsTheEmptyText()
    {
        Queries.GetMessageTypesAsync(Arg.Any<CancellationToken>()).Returns(Ok<IReadOnlyList<RegistryMessageType>>([]));

        var cut = Render<MessageTypeListPage>();

        Assert.Equal("There are no message types.", cut.Find("[data-state=empty]").TextContent);
    }

    [Fact]
    public void Render_Rejected_ShowsTheTextOfRejected()
    {
        Queries.GetMessageTypesAsync(Arg.Any<CancellationToken>()).Returns(Fail<IReadOnlyList<RegistryMessageType>>(Errors.Rejected));

        var cut = Render<MessageTypeListPage>();

        Assert.Equal(AdminErrors.RejectedText, cut.Find(".alert-danger").TextContent);
    }

    private static string TextOf(AngleSharp.Dom.IElement cell) =>
        cell.QuerySelectorAll("[data-channel]") is { Length: > 0 } channels
            ? string.Join(' ', channels.Select(static channel => channel.TextContent.Trim()))
            : cell.TextContent.Trim();
}
