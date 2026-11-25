using MsgHatch.Channels.Email;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Routing;
using MsgHatch.Subscriptions.Api.Contracts.Tests.Unit.TestTypes;
using MsgHatch.Subscriptions.Management;
using MsgHatch.Subscriptions.Tests.Unit.TestTypes;

namespace MsgHatch.Subscriptions.Api.Contracts.Tests.Unit;

/// <summary>The mappings of a channel response and of a message type response in both directions (D228, D229).</summary>
public sealed class ChannelAndMessageTypeResponseUnitTests(ScenarioRegistry scenario) : IClassFixture<ScenarioRegistry>
{
    [Fact]
    public void ToChannelDescriptor_RegisteredChannel_GivesItsValuesAndTheSettingsTypeOfTheRegistry()
    {
        // Arrange
        var email = new ChannelDescriptor<EmailChannelSettings>(EmailChannel.Id, "Email", ChannelFeatures.Languages | ChannelFeatures.RichText, "Mail", Enabled: false);

        // Act
        var descriptor = ChannelResponse.From(email).ToChannelDescriptor(scenario.Registry).Value;

        // Assert
        Assert.Equal(
            (email.Id, email.Name, email.Features, email.Description, email.Enabled, typeof(EmailChannelSettings)),
            (descriptor.Id, descriptor.Name, descriptor.Features, descriptor.Description, descriptor.Enabled, descriptor.SettingsType));
    }

    [Fact]
    public void ToChannelDescriptor_ChannelWithNoSettingsTypeInTheRegistry_GivesTheSettingsTypeChannelSettings()
    {
        // Arrange
        var response = new ChannelResponse("Sms", "SMS", ChannelFeatures.None, Description: null, Enabled: true);

        // Act
        var descriptor = response.ToChannelDescriptor(scenario.Registry);

        // Assert
        Assert.Equal(typeof(ChannelSettings), descriptor.Value.SettingsType);
    }

    [Fact]
    public void ToChannelDescriptor_IdThatIsNotAName_GivesSerializationFailed()
    {
        // Arrange
        var response = new ChannelResponse("S-m-s", "SMS", ChannelFeatures.None, Description: null, Enabled: true);

        // Act
        var descriptor = response.ToChannelDescriptor(scenario.Registry);

        // Assert
        Assert.Equal(Errors.SerializationFailed.Code, descriptor.Error?.Type.Code);
    }

    [Fact]
    public void ToMessageTypeState_ResponseOfAState_GivesTheSameValues()
    {
        // Arrange
        var state = new MessageTypeState(new MessageType("B"), [EmailChannel.Id, new ChannelId("SignalR")], [Scenario.S4, Scenario.S10]);

        // Act
        var roundTrip = MessageTypeResponse.From(state, scenario.Registry).ToMessageTypeState(scenario.Registry).Value;

        // Assert
        Assert.Equal(
            (state.Type, string.Join(", ", state.Channels), string.Join(", ", state.AppliedSubscriptions)),
            (roundTrip.Type, string.Join(", ", roundTrip.Channels), string.Join(", ", roundTrip.AppliedSubscriptions)));
    }

    [Theory]
    [InlineData("1B", "Email")]
    [InlineData("B", "E-mail")]
    public void ToMessageTypeState_NameThatIsNotValid_GivesSerializationFailed(string messageType, string channel)
    {
        // Arrange
        var response = new MessageTypeResponse(messageType, [channel], []);

        // Act
        var state = response.ToMessageTypeState(scenario.Registry);

        // Assert
        Assert.Equal(Errors.SerializationFailed.Code, state.Error?.Type.Code);
    }
}
