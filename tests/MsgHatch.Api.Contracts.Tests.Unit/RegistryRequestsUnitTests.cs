using MsgHatch.Api.Contracts;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Routing;

namespace MsgHatch.Api.Contracts.Tests.Unit;

/// <summary>The bodies of the save routes of the registry from the entries of the commands (D228, D284).</summary>
public sealed class RegistryRequestsUnitTests
{
    private static readonly DateTime _modifiedUtc = new(2026, 11, 18, 13, 32, 0, DateTimeKind.Utc);

    [Fact]
    public void From_ChannelEntry_GivesTheBodyWithTheValuesAndTheExpectedRevision()
    {
        // Act
        var body = ChannelEntryRequest.From(new ChannelEntry(new ChannelId("Email"), "Értesítő", "Stored description", Enabled: false, Revision: 3, _modifiedUtc));

        // Assert
        Assert.Equal(new ChannelEntryRequest(Enabled: false, Revision: 3, "Értesítő", "Stored description"), body);
    }

    [Fact]
    public void From_MessageTypeEntry_GivesTheBodyWithTheNamesOfTheDisabledChannels()
    {
        // Act
        var body = MessageTypeEntryRequest.From(
            new MessageTypeEntry(new MessageType("Invoices"), null, "Sent invoices", Enabled: true, [new ChannelId("Email"), new ChannelId("Sms")], Revision: 2, _modifiedUtc));

        // Assert
        Assert.Equal(
            (true, 2, (string?)null, "Sent invoices", "Email,Sms"),
            (body.Enabled, body.Revision, body.DisplayName, body.Description, string.Join(',', body.DisabledChannels!)));
    }
}
