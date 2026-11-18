using MsgHatch.Api.Contracts;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Management;
using MsgHatch.Domain.Routing;

namespace MsgHatch.Api.Contracts.Tests.Unit;

/// <summary>The mappings of the GET bodies of the registry to the domain types and back (D228, D270).</summary>
public sealed class RegistryResponsesUnitTests
{
    private static readonly ChannelId _email = new("Email");
    private static readonly ChannelId _sms = new("Sms");

    public static TheoryData<string, RegistryChannelResponse> InvalidChannelRows => new()
    {
        { "an id that is not a name", AChannelResponse() with { Id = "not a name" } },
        { "no id", AChannelResponse() with { Id = null! } },
        { "no name", AChannelResponse() with { Name = null! } },
    };

    public static TheoryData<string, RegistryMessageTypeResponse> InvalidMessageTypeRows => new()
    {
        { "a name that is not a name", AMessageTypeResponse() with { Name = "not-a-name" } },
        { "no name", AMessageTypeResponse() with { Name = null! } },
        { "a channel that is not a name", AMessageTypeResponse() with { Channels = ["Email", "not a name"] } },
        { "a null channel", AMessageTypeResponse() with { Channels = [null!] } },
        { "no channels", AMessageTypeResponse() with { Channels = null! } },
        { "a disabled channel that is not a name", AMessageTypeResponse() with { DisabledChannels = ["not a name"] } },
        { "no disabled channels", AMessageTypeResponse() with { DisabledChannels = null! } },
    };

    [Fact]
    public void ToChannel_BodyOfAChannel_GivesTheChannel()
    {
        // Arrange
        var channel = new RegistryChannel(_email, "Email", "Sends emails", ChannelFeatures.Languages | ChannelFeatures.RichText, Enabled: false, Revision: 3);

        // Act
        var mapped = RegistryChannelResponse.From(channel).ToChannel();

        // Assert
        Assert.Equal(channel, mapped.Value);
    }

    [Fact]
    public void ToMessageType_BodyOfAMessageType_GivesTheMessageType()
    {
        // Arrange
        var type = new RegistryMessageType(new MessageType("Invoices"), "Invoices", "Sent invoices", [_email, _sms], Enabled: true, [_sms], Revision: 2);

        // Act
        var mapped = RegistryMessageTypeResponse.From(type).ToMessageType();

        // Assert
        Assert.Equal(
            (type.Type, type.DisplayName, type.Description, "Email,Sms", type.Enabled, "Sms", type.Revision),
            (mapped.Value.Type, mapped.Value.DisplayName, mapped.Value.Description, string.Join(',', mapped.Value.Channels.Select(static channel => channel.Name)),
                mapped.Value.Enabled, string.Join(',', mapped.Value.DisabledChannels.Select(static channel => channel.Name)), mapped.Value.Revision));
    }

    [Theory]
    [MemberData(nameof(InvalidChannelRows))]
    public void ToChannel_ValueThatIsNotValid_GivesSerializationFailed(string row, RegistryChannelResponse body)
    {
        // Act
        var mapped = body.ToChannel();

        // Assert
        Assert.Equal((row, Errors.SerializationFailed.Code), (row, mapped.Error?.Type.Code));
    }

    [Theory]
    [MemberData(nameof(InvalidMessageTypeRows))]
    public void ToMessageType_ValueThatIsNotValid_GivesSerializationFailed(string row, RegistryMessageTypeResponse body)
    {
        // Act
        var mapped = body.ToMessageType();

        // Assert
        Assert.Equal((row, Errors.SerializationFailed.Code), (row, mapped.Error?.Type.Code));
    }

    private static RegistryChannelResponse AChannelResponse() => new("Email", "Email", null, ChannelFeatures.None, Enabled: true, Revision: 0);

    private static RegistryMessageTypeResponse AMessageTypeResponse() => new("Invoices", null, null, ["Email"], Enabled: true, [], Revision: 0);
}
