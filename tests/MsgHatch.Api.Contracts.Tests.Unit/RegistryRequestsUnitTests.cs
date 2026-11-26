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
        // Arrange
        const string displayName = "Értesítő";
        const string description = "Stored description";
        const int revision = 3;
        var entry = new ChannelEntry(new ChannelId("Email"), displayName, description, Enabled: false, revision, _modifiedUtc);

        // Act
        var body = ChannelEntryRequest.From(entry);

        // Assert
        Assert.Equal(new ChannelEntryRequest(Enabled: false, revision, displayName, description), body);
    }

    [Fact]
    public void From_MessageTypeEntry_GivesTheBodyWithTheNamesOfTheDisabledChannels()
    {
        // Arrange
        const string description = "Sent invoices";
        const string email = "Email";
        const string sms = "Sms";
        const int revision = 2;
        var entry = new MessageTypeEntry(
            new MessageType("Invoices"), null, description, Enabled: true, [new ChannelId(email), new ChannelId(sms)], revision, _modifiedUtc);

        // Act
        var body = MessageTypeEntryRequest.From(entry);

        // Assert
        Assert.Equal(
            (true, revision, (string?)null, description, $"{email},{sms}"),
            (body.Enabled, body.Revision, body.DisplayName, body.Description, string.Join(',', body.DisabledChannels!)));
    }
}
