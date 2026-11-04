using MsgHatch.Broker;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Routing;
using MsgHatch.Email;
using MsgHatch.SignalR;
using MsgHatch.Subscriptions.Tests.Unit.TestTypes;

namespace MsgHatch.Subscriptions.Tests.Unit;

/// <summary>The JSON of the channel settings of a subscription, by the content type <c>Settings:{Channel}</c> and its version (D149).</summary>
public sealed class ChannelSettingsJsonUnitTests
{
    private static readonly ChannelSettingsJson _json = ScenarioContentTypes.SettingsJson;

    public static TheoryData<string> ScenarioChannels => ["Email", "SignalR", "Broker"];

    public static TheoryData<string, string, int, string, int> ReadFailureRows => new()
    {
        { "newer version", "Settings:Email", 2, "{}", 426 },
        { "unknown content type", "Settings:Pager", 1, "{}", 415 },
        { "content type that is not a settings type", "A", 1, "{}", 415 },
        { "invalid JSON", "Settings:Email", 1, "{", 415 },
        { "JSON null", "Settings:Email", 1, "null", 415 },
    };

    [Theory]
    [MemberData(nameof(ScenarioChannels))]
    public void Deserialize_SerializedSettingsOfAScenarioChannel_GivesTheSameSettingsWithVersion1(string channel)
    {
        // Arrange
        var written = _json.Serialize(new ChannelId(channel), SettingsOf(channel)).Value;

        // Act
        var read = _json.Deserialize(written.ContentType, written.Version, written.Json);

        // Assert
        Assert.Equal(new SerializedSettings($"Settings:{channel}", 1, written.Json), _json.Serialize(new ChannelId(channel), read.Value).Value);
    }

    [Fact]
    public void Deserialize_EmailSettings_GivesEqualSettings()
    {
        // Arrange
        var settings = new EmailChannelSettings("en", EmailFormat.PlainText);
        var written = _json.Serialize(EmailChannel.Id, settings).Value;

        // Act
        var read = _json.Deserialize(written.ContentType, written.Version, written.Json);

        // Assert
        Assert.Equal(settings, read.Value);
    }

    [Fact]
    public void Serialize_UnregisteredChannel_GivesSerializationFailed()
    {
        // Arrange
        var settings = new TestChannelSettings();

        // Act
        var written = _json.Serialize(TestSubscriptions.TestChannel, settings);

        // Assert
        Assert.Equal(415, written.Error?.Type.Code);
    }

    [Fact]
    public void Serialize_SettingsOfAnotherChannel_GivesSerializationFailed()
    {
        // Arrange
        var settings = new SignalRChannelSettings("hu");

        // Act
        var written = _json.Serialize(EmailChannel.Id, settings);

        // Assert
        Assert.Equal(415, written.Error?.Type.Code);
    }

    [Theory]
    [MemberData(nameof(ReadFailureRows))]
    public void Deserialize_RecordThatTheCodeCannotRead_GivesTheErrorOfTheRow(string row, string contentType, int version, string json, int expectedCode)
    {
        // Act
        var read = _json.Deserialize(contentType, version, json);

        // Assert
        Assert.Equal((row, (int?)expectedCode), (row, read.Error?.Type.Code));
    }

    private static ChannelSettings SettingsOf(string channel) => channel switch
    {
        "Email" => new EmailChannelSettings("en", EmailFormat.PlainText),
        "SignalR" => new SignalRChannelSettings("hu"),
        _ => new BrokerChannelSettings(new Dictionary<string, string> { ["tenant"] = "Ődön \"<&>\"" }),
    };
}
