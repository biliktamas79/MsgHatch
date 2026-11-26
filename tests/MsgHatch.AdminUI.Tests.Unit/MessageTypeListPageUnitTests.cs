using Bunit;
using MsgHatch.AdminUI.Tests.Unit.TestTypes;
using Microsoft.Extensions.DependencyInjection;
using MsgHatch.AdminUI.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Management;
using NSubstitute;

namespace MsgHatch.AdminUI.Tests.Unit;

public sealed class MessageTypeListPageUnitTests : RegistryPageContext
{
    [Fact]
    public void Render_MessageTypes_ShowsEachRowWithItsDisplayNameItsDescriptionItsEnabledFlagAndItsChannels()
    {
        // Arrange
        const string description = "Sent when a client asks";
        Queries.GetMessageTypesAsync(Arg.Any<CancellationToken>()).Returns(Ok<IReadOnlyList<RegistryMessageType>>(
            [MessageTypes[0] with { Description = description }, MessageTypes[1]]));

        // Act
        var cut = Render<MessageTypeListPage>();

        // Assert
        var rows = cut.FindAll("tr[data-row]");
        Assert.Equal(["Requests", "Requests of a client", description, "Yes", "Email Sms", "Edit"], rows[0].Children.Select(TextOf));
        Assert.Equal(["Notices", "", "", "No", "Email", "Edit"], rows[1].Children.Select(TextOf));
        Assert.Equal("Message types", cut.Find("[data-list-header] h1").TextContent);
    }

    [Fact]
    public void Render_ChannelsOfAMessageType_LinkToTheirViewPages()
    {
        // Act
        var cut = Render<MessageTypeListPage>();

        // Assert
        Assert.Equal(
            [RegistryAdminRoutes.Channel(Email).TrimStart('/'), RegistryAdminRoutes.Channel(Sms).TrimStart('/')],
            cut.FindAll("tr[data-row]")[0].QuerySelectorAll("[data-channel] a").Select(static link => link.GetAttribute("href")));
    }

    [Fact]
    public void Render_MessageTypes_EndEachRowWithTheAddressOfItsEditPage()
    {
        // Act
        var cut = Render<MessageTypeListPage>();

        // Assert
        Assert.Equal(
            MessageTypes.Select(static type => RegistryAdminRoutes.MessageTypeEdit(type.Type).TrimStart('/')),
            cut.FindAll("tr[data-row]").Select(static row => row.Children[^1].QuerySelector("[data-action=edit]")?.GetAttribute("href")));
    }

    [Fact]
    public void Render_DisabledChannelOfAMessageType_HasTheMarkDisabled()
    {
        // Act
        var cut = Render<MessageTypeListPage>();

        // Assert
        Assert.Equal(
            ("disabled", 1),
            (cut.Find("tr[data-row]:nth-child(3) [data-channel=Email] [data-disabled]").TextContent, cut.FindAll("[data-disabled]").Count));
    }

    [Fact]
    public void Render_NoMessageType_ShowsTheEmptyText()
    {
        // Arrange
        Queries.GetMessageTypesAsync(Arg.Any<CancellationToken>()).Returns(Ok<IReadOnlyList<RegistryMessageType>>([]));

        // Act
        var cut = Render<MessageTypeListPage>();

        // Assert
        Assert.Equal("There are no message types.", cut.Find("[data-state=empty]").TextContent);
    }

    [Fact]
    public void Render_Rejected_ShowsTheTextOfRejected()
    {
        // Arrange
        Queries.GetMessageTypesAsync(Arg.Any<CancellationToken>()).Returns(Fail<IReadOnlyList<RegistryMessageType>>(Errors.Rejected));

        // Act
        var cut = Render<MessageTypeListPage>();

        // Assert
        Assert.Equal(AdminErrors.RejectedText, cut.Find(".alert-danger").TextContent);
    }

    [Fact]
    public void Render_TwoColumns_ShowsThemAfterTheOwnColumnsInTheOrderOfRegistration()
    {
        // Arrange
        AddColumns(Ok(), Ok());

        // Act
        var cut = Render<MessageTypeListPage>();

        // Assert
        Assert.Equal(
            ["Message type", "Display name", "Description", "Enabled", "Channels", "First", "Second", ""],
            cut.FindAll("thead th").Select(static header => header.TextContent.Trim()));
    }

    [Fact]
    public void Render_ColumnItems_ShowsEachItemAsABadgeAndAnItemWithAnAddressAsALink()
    {
        // Arrange
        const string linkText = "Default 1";
        const string address = "msghatch/subscriptions?type=Requests&level=Default";
        const string badgeText = "Note";
        AddColumns(Ok((MessageTypes[0].Type, [new(linkText, address), new(badgeText, null)])), Ok());

        // Act
        var cut = Render<MessageTypeListPage>();

        // Assert
        var items = cut.FindAll("tr[data-row]")[0].QuerySelectorAll("td[data-column=First] [data-item]");
        Assert.Equal(
            [("A", linkText, address, true), ("SPAN", badgeText, null, true)],
            items.Select(static item => (item.TagName, item.TextContent, item.GetAttribute("href"), item.ClassList.Contains("badge"))));
        Assert.Empty(cut.FindAll("tr[data-row]")[1].QuerySelectorAll("td[data-column=First] [data-item]"));
    }

    [Fact]
    public void Render_ColumnError_ShowsTheErrorAboveTheListWithItsHeaderAndTheOtherColumnStillShows()
    {
        // Arrange
        const string itemText = "Second item";
        AddColumns(
            ErrorOr<IReadOnlyDictionary<MessageType, IReadOnlyList<MessageTypeCellItem>>>.ForError(new Error(Errors.Unavailable)),
            Ok((MessageTypes[0].Type, [new(itemText, null)])));

        // Act
        var cut = Render<MessageTypeListPage>();

        // Assert
        var row = cut.FindAll("tr[data-row]")[0];
        Assert.Equal(
            ($"First: {AdminErrors.UnavailableText}", "First", 0, itemText),
            (cut.Find("[data-column-error]").TextContent, cut.Find("[data-column-error]").GetAttribute("data-column-error"),
                row.QuerySelectorAll("td[data-column=First] [data-item]").Length, row.QuerySelector("td[data-column=Second] [data-item]")!.TextContent));
    }

    [Fact]
    public void AddMessageTypeColumn_SameColumnTwoTimes_AddsItOneTime()
    {
        // Arrange
        AddColumns(Ok(), Ok());
        Services.AddMessageTypeColumn<FirstTestColumn>();

        // Act
        var cut = Render<MessageTypeListPage>();

        // Assert
        Assert.Single(cut.FindAll("thead th[data-column=First]"));
    }

    private void AddColumns(
        ErrorOr<IReadOnlyDictionary<MessageType, IReadOnlyList<MessageTypeCellItem>>> first,
        ErrorOr<IReadOnlyDictionary<MessageType, IReadOnlyList<MessageTypeCellItem>>> second)
    {
        var cells = new TestColumnCells();
        (cells.ByHeader["First"], cells.ByHeader["Second"]) = (first, second);
        Services.AddSingleton(cells);
        Services.AddMessageTypeColumn<FirstTestColumn>().AddMessageTypeColumn<SecondTestColumn>();
    }

    private static ErrorOr<IReadOnlyDictionary<MessageType, IReadOnlyList<MessageTypeCellItem>>> Ok(
        params (MessageType Type, MessageTypeCellItem[] Items)[] cells) =>
        ErrorOr<IReadOnlyDictionary<MessageType, IReadOnlyList<MessageTypeCellItem>>>.ForValue(
            cells.ToDictionary(static cell => cell.Type, static cell => (IReadOnlyList<MessageTypeCellItem>)cell.Items));

    private static string TextOf(AngleSharp.Dom.IElement cell) =>
        cell.QuerySelectorAll("[data-channel]") is { Length: > 0 } channels
            ? string.Join(' ', channels.Select(static channel => channel.TextContent.Trim()))
            : cell.TextContent.Trim();
}
