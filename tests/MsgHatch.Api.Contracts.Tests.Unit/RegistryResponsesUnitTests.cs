using MsgHatch.Api.Contracts;
using MsgHatch.Domain.Dispatch;
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
    private static readonly DateTime _sinceUtc = new(2026, 11, 18, 13, 32, 0, DateTimeKind.Utc);

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

    [Theory]
    [MemberData(nameof(StateRows))]
    public void ToChannel_BodyOfAChannelWithACurrentRow_GivesTheChannelWithThatRow(ChannelState current)
    {
        // Arrange
        var channel = new RegistryChannel(_sms, "Text messages", null, ChannelFeatures.None, Enabled: true, Revision: 0, current);

        // Act
        var mapped = RegistryChannelResponse.From(channel).ToChannel();

        // Assert
        Assert.Equal(channel, mapped.Value);
    }

    [Fact]
    public void ToChannel_CurrentRowThatIsNotValid_GivesSerializationFailed()
    {
        // Arrange
        var body = AChannelResponse() with { Current = new ChannelStateResponse("Email", 1, ChannelStateKind.Down, _sinceUtc, null, null, null, null, null, null, null) };

        // Act
        var mapped = body.ToChannel();

        // Assert
        Assert.Equal(Errors.SerializationFailed.Code, mapped.Error?.Type.Code);
    }

    [Theory]
    [InlineData(ChannelDownReason.Errors)]
    [InlineData(ChannelDownReason.Operator)]
    public void ToDown_BodyOfADownState_GivesTheDownStateOfTheChannel(ChannelDownReason reason)
    {
        // Arrange
        var down = reason == ChannelDownReason.Errors
            ? new ChannelDown(_email, reason, _sinceUtc, _sinceUtc.AddSeconds(30), 503, "Unavailable", Note: null)
            : new ChannelDown(_email, reason, _sinceUtc, NextCheckAtUtc: null, ErrorCode: null, ErrorName: null, "Ősz-űr maintenance");

        // Act
        var mapped = ChannelDownResponse.From(down).ToDown(_email);

        // Assert
        Assert.Equal(down, mapped);
    }

    public static TheoryData<ChannelState> StateRows => new()
    {
        new ChannelState(_sms, 1, ChannelStateKind.Down, _sinceUtc, null, null, ChannelDownReason.Errors, _sinceUtc.AddSeconds(30), 503, "Unavailable", null),
        new ChannelState(_sms, 2, ChannelStateKind.Up, _sinceUtc, ChannelStateKind.Down, TimeSpan.FromMilliseconds(5_400_250), null, null, null, null, null),
        new ChannelState(_sms, 3, ChannelStateKind.Down, _sinceUtc, ChannelStateKind.Up, TimeSpan.Zero, ChannelDownReason.Operator, null, null, null, "Ősz-űr maintenance"),
    };

    [Theory]
    [MemberData(nameof(StateRows))]
    public void ToState_BodyOfAStateRow_GivesTheRow(ChannelState row)
    {
        // Act
        var mapped = ChannelStateResponse.From(row).ToState();

        // Assert
        Assert.Equal(row, mapped.Value);
    }

    [Fact]
    public void From_RowWithAPreviousDuration_GivesItInWholeMilliseconds()
    {
        // Act
        var body = ChannelStateResponse.From(
            new ChannelState(_sms, 2, ChannelStateKind.Up, _sinceUtc, ChannelStateKind.Down, TimeSpan.FromMinutes(90), null, null, null, null, null));

        // Assert
        Assert.Equal(5_400_000, body.PreviousDurationMs);
    }

    [Theory]
    [InlineData("not a name", ChannelStateKind.Up, null)]
    [InlineData(null, ChannelStateKind.Up, null)]
    [InlineData("Sms", ChannelStateKind.Down, null)]
    public void ToState_ValueThatIsNotValid_GivesSerializationFailed(string? channel, ChannelStateKind kind, ChannelDownReason? reason)
    {
        // Arrange
        var body = new ChannelStateResponse(channel!, 1, kind, _sinceUtc, null, null, reason, null, null, null, null);

        // Act
        var mapped = body.ToState();

        // Assert
        Assert.Equal(Errors.SerializationFailed.Code, mapped.Error?.Type.Code);
    }

    [Theory]
    [InlineData(null, null, false)]
    [InlineData(503, "Unavailable", true)]
    public void ToCheck_BodyOfACheck_GivesTheCheckOfTheChannel(int? errorCode, string? errorName, bool changed)
    {
        // Arrange
        var check = new ChannelCheck(_sms, errorCode, errorName, changed);

        // Act
        var mapped = ChannelCheckResponse.From(check).ToCheck();

        // Assert
        Assert.Equal(check, mapped.Value);
    }

    [Theory]
    [InlineData("not a name", null, null)]
    [InlineData(null, null, null)]
    [InlineData("Sms", 503, null)]
    [InlineData("Sms", null, "Unavailable")]
    public void ToCheck_ValueThatIsNotValid_GivesSerializationFailed(string? channel, int? errorCode, string? errorName)
    {
        // Arrange
        var body = new ChannelCheckResponse(channel!, errorCode, errorName, Changed: false);

        // Act
        var mapped = body.ToCheck();

        // Assert
        Assert.Equal(Errors.SerializationFailed.Code, mapped.Error?.Type.Code);
    }

    [Fact]
    public void ToEntry_BodyOfAChannelEntry_GivesTheEntry()
    {
        // Arrange
        var entry = new ChannelEntry(_email, "Értesítő", "Stored description", Enabled: false, Revision: 4, _sinceUtc);

        // Act
        var mapped = ChannelEntryResponse.From(entry).ToEntry();

        // Assert
        Assert.Equal(entry, mapped.Value);
    }

    [Fact]
    public void ToEntry_BodyOfAMessageTypeEntry_GivesTheEntry()
    {
        // Arrange
        var entry = new MessageTypeEntry(new MessageType("Invoices"), null, "Stored description", Enabled: true, [_email, _sms], Revision: 2, _sinceUtc);

        // Act
        var mapped = MessageTypeEntryResponse.From(entry).ToEntry();

        // Assert
        Assert.Equal(TextOf(entry), TextOf(mapped.Value));
    }

    public static TheoryData<string, ChannelEntryResponse> InvalidChannelEntryRows => new()
    {
        { "a channel that is not a name", AChannelEntryResponse() with { Channel = "not a name" } },
        { "no channel", AChannelEntryResponse() with { Channel = null! } },
    };

    [Theory]
    [MemberData(nameof(InvalidChannelEntryRows))]
    public void ToEntry_ChannelEntryValueThatIsNotValid_GivesSerializationFailed(string row, ChannelEntryResponse body)
    {
        // Act
        var mapped = body.ToEntry();

        // Assert
        Assert.Equal((row, Errors.SerializationFailed.Code), (row, mapped.Error?.Type.Code));
    }

    public static TheoryData<string, MessageTypeEntryResponse> InvalidMessageTypeEntryRows => new()
    {
        { "a type that is not a name", AMessageTypeEntryResponse() with { Type = "not-a-name" } },
        { "no type", AMessageTypeEntryResponse() with { Type = null! } },
        { "a disabled channel that is not a name", AMessageTypeEntryResponse() with { DisabledChannels = ["not a name"] } },
        { "no disabled channels", AMessageTypeEntryResponse() with { DisabledChannels = null! } },
    };

    [Theory]
    [MemberData(nameof(InvalidMessageTypeEntryRows))]
    public void ToEntry_MessageTypeEntryValueThatIsNotValid_GivesSerializationFailed(string row, MessageTypeEntryResponse body)
    {
        // Act
        var mapped = body.ToEntry();

        // Assert
        Assert.Equal((row, Errors.SerializationFailed.Code), (row, mapped.Error?.Type.Code));
    }

    [Fact]
    public void ToChannel_BodyOfAChannelWithAnEntry_GivesTheChannelWithItsEntry()
    {
        // Arrange
        var entry = new ChannelEntry(_email, "Shop email", null, Enabled: true, Revision: 2, _sinceUtc);
        var channel = new RegistryChannel(_email, "Shop email", null, ChannelFeatures.None, Enabled: true, Revision: 2, Entry: entry);

        // Act
        var mapped = RegistryChannelResponse.From(channel).ToChannel();

        // Assert
        Assert.Equal(channel, mapped.Value);
    }

    [Fact]
    public void ToMessageType_BodyOfAMessageTypeWithAnEntry_GivesTheMessageTypeWithItsEntry()
    {
        // Arrange
        var entry = new MessageTypeEntry(new MessageType("Invoices"), "Invoices", null, Enabled: false, [_sms], Revision: 5, _sinceUtc);
        var type = new RegistryMessageType(entry.Type, "Invoices", null, [_email, _sms], Enabled: false, [_sms], Revision: 5, entry);

        // Act
        var mapped = RegistryMessageTypeResponse.From(type).ToMessageType();

        // Assert
        Assert.Equal(TextOf(entry), TextOf(mapped.Value.Entry!));
    }

    [Fact]
    public void ToChannelAndToMessageType_EntryThatIsNotValid_GiveSerializationFailed()
    {
        // Arrange
        var channel = AChannelResponse() with { Entry = AChannelEntryResponse() with { Channel = "not a name" } };
        var type = AMessageTypeResponse() with { Entry = AMessageTypeEntryResponse() with { DisabledChannels = ["not a name"] } };

        // Act
        var mappedChannel = channel.ToChannel();
        var mappedType = type.ToMessageType();

        // Assert
        Assert.Equal((Errors.SerializationFailed.Code, Errors.SerializationFailed.Code), (mappedChannel.Error?.Type.Code, mappedType.Error?.Type.Code));
    }

    private static string TextOf(MessageTypeEntry entry) =>
        $"{entry.Type.Name} {entry.DisplayName} {entry.Description} {entry.Enabled} "
        + $"{string.Join(',', entry.DisabledChannels.Select(static channel => channel.Name))} {entry.Revision} {entry.LastModifiedAtUtc:O}";

    private static ChannelEntryResponse AChannelEntryResponse() => new("Email", null, null, Enabled: true, Revision: 1, _sinceUtc);

    private static MessageTypeEntryResponse AMessageTypeEntryResponse() => new("Invoices", null, null, Enabled: true, ["Email"], Revision: 1, _sinceUtc);

    private static RegistryChannelResponse AChannelResponse() => new("Email", "Email", null, ChannelFeatures.None, Enabled: true, Revision: 0);

    private static RegistryMessageTypeResponse AMessageTypeResponse() => new("Invoices", null, null, ["Email"], Enabled: true, [], Revision: 0);
}
