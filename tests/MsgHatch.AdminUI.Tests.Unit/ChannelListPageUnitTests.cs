using Bunit;
using MsgHatch.AdminUI.Tests.Unit.TestTypes;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Management;
using NSubstitute;

namespace MsgHatch.AdminUI.Tests.Unit;

public sealed class ChannelListPageUnitTests : RegistryPageContext
{
    [Fact]
    public void Render_Channels_ShowsEachRowWithItsFeaturesItsEnabledFlagItsMessageTypeCountAndTheAddressOfView()
    {
        var cut = Render<ChannelListPage>();

        var rows = cut.FindAll("tr[data-row]");
        Assert.Equal(["Email", "Email channel", "LanguagesRichText", "Yes", "3", "View"], rows[0].Children.Select(static cell => cell.TextContent.Trim()));
        Assert.Equal(["Sms", "Text messages", "", "No", "1", "View"], rows[1].Children.Select(static cell => cell.TextContent.Trim()));
        Assert.Equal(["Fax", "Fax", "GroupDelivery", "Yes", "0", "View"], rows[2].Children.Select(static cell => cell.TextContent.Trim()));
        Assert.Equal(
            ("Channels", 2, RegistryAdminRoutes.Channel(Email).TrimStart('/')),
            (cut.Find("[data-list-header] h1").TextContent, rows[0].QuerySelectorAll("[data-feature].badge").Length,
                rows[0].QuerySelector("[data-action=view]")?.GetAttribute("href")));
    }

    [Fact]
    public void Render_NoChannel_ShowsTheEmptyText()
    {
        Queries.GetChannelsAsync(Arg.Any<CancellationToken>()).Returns(Ok<IReadOnlyList<RegistryChannel>>([]));

        var cut = Render<ChannelListPage>();

        Assert.Equal("There are no channels.", cut.Find("[data-state=empty]").TextContent);
    }

    [Fact]
    public void Render_ChannelsUnavailable_ShowsTheTextOfUnavailable()
    {
        Queries.GetChannelsAsync(Arg.Any<CancellationToken>()).Returns(Fail<IReadOnlyList<RegistryChannel>>(Errors.Unavailable));

        var cut = Render<ChannelListPage>();

        Assert.Equal(AdminErrors.UnavailableText, cut.Find(".alert-danger").TextContent);
    }

    [Fact]
    public void Render_MessageTypesRejected_ShowsTheTextOfRejected()
    {
        Queries.GetMessageTypesAsync(Arg.Any<CancellationToken>()).Returns(Fail<IReadOnlyList<RegistryMessageType>>(Errors.Rejected));

        var cut = Render<ChannelListPage>();

        Assert.Equal((AdminErrors.RejectedText, 0), (cut.Find(".alert-danger").TextContent, cut.FindAll("tr[data-row]").Count));
    }
}
