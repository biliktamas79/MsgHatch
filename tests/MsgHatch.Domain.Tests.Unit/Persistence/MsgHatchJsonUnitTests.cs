using System.Text.Json;
using System.Text.Json.Nodes;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Persistence;
using MsgHatch.Domain.Routing;
using MsgHatch.Domain.Tests.Unit.TestTypes;

namespace MsgHatch.Domain.Tests.Unit.Persistence;

public sealed class MsgHatchJsonUnitTests
{
    private const string _specialText = "Ősz-űr \"quoted\" back\\slash <tag> & more";

    private readonly MsgHatchJson _json = new(TestContentTypes.ARegistry());

    public static TheoryData<string, string> UnknownDiscriminatorRows => new()
    {
        { "trace.$type", "Trace:Unknown@1" },
        { "trace.$type", "not a discriminator" },
        { "contentType", "Test:Unknown" },
    };

    public static TheoryData<string, int> NewerOutgoingVersionRows => new()
    {
        { "contentVersion", 2 },
        { "schemaVersion", 2 },
    };

    public static TheoryData<string, int> NewerReceivedVersionRows => new()
    {
        { "contentVersion", 3 },
        { "schemaVersion", 2 },
    };

    [Fact]
    public void DeserializeOutgoing_SerializedMessage_GivesAnEqualMessageOfTheClosedType()
    {
        // Arrange
        var message = TestMessages.AnOutgoingMessage(
            new TestEnvelope(_specialText), tenant: new TenantId("t1"), flags: MessageFlags.Test,
            trace: new TestTrace("TestRouter"), delivery: AFullDeliveryState());
        var json = _json.Serialize(message).Value;

        // Act
        var read = _json.DeserializeOutgoing(json);

        // Assert
        Assert.True(read.IsSuccess);
        Assert.IsType<OutgoingMessage<TestEnvelope>>(read.Value);
        Assert.Equal(message, read.Value);
    }

    [Fact]
    public void DeserializeReceived_SerializedMessage_GivesAnEqualMessageOfTheClosedType()
    {
        // Arrange
        var message = TestMessages.AReceivedMessage(
            new TestEvent(_specialText), tenant: new TenantId("t1"), flags: MessageFlags.Test);
        var json = _json.Serialize(message).Value;

        // Act
        var read = _json.DeserializeReceived(json);

        // Assert
        Assert.True(read.IsSuccess);
        Assert.IsType<ReceivedMessage<TestEvent>>(read.Value);
        Assert.Equal(message, read.Value);
    }

    [Fact]
    public void Serialize_OutgoingMessage_WritesCamelCaseNamesWithNullValuesAndNoContent()
    {
        // Arrange
        var message = TestMessages.AnOutgoingMessage();

        // Act
        var json = _json.Serialize(message).Value;

        // Assert
        using var document = JsonDocument.Parse(json);
        string[] expected =
        [
            "schemaVersion", "delivery", "sourceMessageId", "tenant", "type", "channel", "target", "contentType",
            "contentVersion", "flags", "traceParent", "trace", "envelope",
        ];
        Assert.Equal(expected, document.RootElement.EnumerateObject().Select(static p => p.Name));
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("tenant").ValueKind);
    }

    [Fact]
    public void Serialize_ReceivedMessage_WritesCamelCaseNames()
    {
        // Arrange
        var message = TestMessages.AReceivedMessage(new TestEvent("text"));

        // Act
        var json = _json.Serialize(message).Value;

        // Assert
        using var document = JsonDocument.Parse(json);
        string[] expected = ["schemaVersion", "delivery", "tenant", "type", "contentVersion", "flags", "traceParent", "event"];
        Assert.Equal(expected, document.RootElement.EnumerateObject().Select(static p => p.Name));
    }

    [Fact]
    public void Serialize_OutgoingMessage_WritesEnumValuesAsStrings()
    {
        // Arrange
        var message = TestMessages.AnOutgoingMessage(flags: MessageFlags.Test);

        // Act
        var json = _json.Serialize(message).Value;

        // Assert
        var root = JsonNode.Parse(json)!;
        Assert.Equal(
            ["Pending", "User", "Test"],
            [root["delivery"]!["status"]!.GetValue<string>(), root["target"]!["kind"]!.GetValue<string>(),
                root["flags"]!.GetValue<string>()]);
    }

    [Fact]
    public void Serialize_OutgoingMessageWithATrace_WritesTheDiscriminatorAsTheFirstTraceProperty()
    {
        // Arrange
        var message = TestMessages.AnOutgoingMessage(trace: new TestTrace("TestRouter"));

        // Act
        var json = _json.Serialize(message).Value;

        // Assert
        using var document = JsonDocument.Parse(json);
        Assert.Equal(
            ["$type=Trace:Test@1", "reason=TestRouter"],
            document.RootElement.GetProperty("trace").EnumerateObject().Select(static p => $"{p.Name}={p.Value.GetString()}"));
    }

    [Fact]
    public void DeserializeOutgoing_MessageWithNoTrace_HasNoTrace()
    {
        // Arrange
        var message = TestMessages.AnOutgoingMessage(trace: null);
        var json = _json.Serialize(message).Value;

        // Act
        var read = _json.DeserializeOutgoing(json);

        // Assert
        Assert.Null(read.Value.Trace);
        Assert.Equal(message, read.Value);
    }

    [Theory]
    [MemberData(nameof(UnknownDiscriminatorRows))]
    public void DeserializeOutgoing_UnknownDiscriminator_ReturnsSerializationFailed(string path, string value)
    {
        // Arrange
        var json = WithValue(_json.Serialize(TestMessages.AnOutgoingMessage(trace: new TestTrace("x"))).Value, path, value);

        // Act
        var read = _json.DeserializeOutgoing(json);

        // Assert
        Assert.Equal(Errors.SerializationFailed, read.Error?.Type);
    }

    [Fact]
    public void Serialize_UnregisteredTraceType_ReturnsSerializationFailed()
    {
        // Arrange
        var message = TestMessages.AnOutgoingMessage(trace: new UnregisteredTrace());

        // Act
        var result = _json.Serialize(message);

        // Assert
        Assert.Equal(Errors.SerializationFailed, result.Error?.Type);
    }

    [Fact]
    public void DeserializeReceived_OldContentVersion_GivesTheOldClosedType()
    {
        // Arrange
        var message = TestMessages.AReceivedMessage(new TestEventV1("old"), contentVersion: 1);
        var json = _json.Serialize(message).Value;

        // Act
        var read = _json.DeserializeReceived(json);

        // Assert
        Assert.IsType<ReceivedMessage<TestEventV1>>(read.Value);
        Assert.Equal(message, read.Value);
    }

    [Theory]
    [MemberData(nameof(NewerReceivedVersionRows))]
    public void DeserializeReceived_NewerVersion_ReturnsUnsupportedVersion(string path, int version)
    {
        // Arrange
        var json = WithValue(_json.Serialize(TestMessages.AReceivedMessage(new TestEvent("x"))).Value, path, version);

        // Act
        var read = _json.DeserializeReceived(json);

        // Assert
        Assert.Equal(Errors.UnsupportedVersion, read.Error?.Type);
    }

    [Theory]
    [MemberData(nameof(NewerOutgoingVersionRows))]
    public void DeserializeOutgoing_NewerVersion_ReturnsUnsupportedVersion(string path, int version)
    {
        // Arrange
        var json = WithValue(_json.Serialize(TestMessages.AnOutgoingMessage()).Value, path, version);

        // Act
        var read = _json.DeserializeOutgoing(json);

        // Assert
        Assert.Equal(Errors.UnsupportedVersion, read.Error?.Type);
    }

    [Fact]
    public void DeserializeOutgoing_TextThatIsNotJson_ReturnsSerializationFailed()
    {
        // Arrange
        var json = "{ not json"u8.ToArray();

        // Act
        var read = _json.DeserializeOutgoing(json);

        // Assert
        Assert.Equal(Errors.SerializationFailed, read.Error?.Type);
    }

    [Fact]
    public void DeserializeReceived_GoldenSampleOfVersion1_GivesTheSampleMessage()
    {
        // Arrange
        var sample = ReadSample("ReceivedMessage.v1.json");
        var expected = new ReceivedMessage<TestEvent>(
            1,
            new DeliveryState(
                Guid.Parse("0b6c1f7e-2a44-4d8e-9a3b-61c0f5e2d7a9"), DeliveryStatus.Pending, 0, 8,
                TestMessages.CreatedAtUtc, null, TestMessages.CreatedAtUtc, null, null, null),
            new TenantId("t1"), new MessageType("TestEvent"), 2, MessageFlags.Test,
            "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01", new TestEvent("Order 1001"));

        // Act
        var read = _json.DeserializeReceived(sample);

        // Assert
        Assert.Equal(expected, read.Value);
    }

    [Fact]
    public void DeserializeOutgoing_GoldenSampleOfVersion1WithATrace_GivesTheSampleMessage()
    {
        // Arrange
        var sample = ReadSample("OutgoingMessage.v1.json");
        var expected = new OutgoingMessage<TestEnvelope>(
            1,
            new DeliveryState(
                Guid.Parse("7d2e4b1a-5c3f-5a9e-8b10-4f6a2d9c0e31"), DeliveryStatus.Pending, 1, 8,
                TestMessages.CreatedAtUtc, TestMessages.CreatedAtUtc.AddSeconds(1), TestMessages.CreatedAtUtc.AddSeconds(6),
                null, null, "RateLimited: Maximum sending rate exceeded."),
            Guid.Parse("0b6c1f7e-2a44-4d8e-9a3b-61c0f5e2d7a9"), null, new MessageType("TestEvent"), new ChannelId("Test"),
            new DeliveryTarget(DeliveryTargetKind.User, "1"), "Test:TestEnvelope", 1, MessageFlags.None,
            "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01", new TestTrace("TestRouter"),
            new TestEnvelope("Order 1001 is shipped"));

        // Act
        var read = _json.DeserializeOutgoing(sample);

        // Assert
        Assert.Equal(expected, read.Value);
    }

    private static DeliveryState AFullDeliveryState() =>
        new(
            Guid.NewGuid(), DeliveryStatus.Failed, TryCount: 2, MaxTryCount: 3, TestMessages.CreatedAtUtc,
            LastTryAtUtc: TestMessages.CreatedAtUtc.AddSeconds(2), NextTryAtUtc: TestMessages.CreatedAtUtc.AddSeconds(7),
            LeaseUntilUtc: TestMessages.CreatedAtUtc.AddSeconds(60), ExpiresAtUtc: TestMessages.CreatedAtUtc.AddDays(1),
            LastError: _specialText);

    private static byte[] WithValue(byte[] json, string path, JsonNode value)
    {
        var root = JsonNode.Parse(json)!;
        var names = path.Split('.');
        var parent = names[..^1].Aggregate(root, static (node, name) => node[name]!);
        parent[names[^1]] = value;
        return JsonSerializer.SerializeToUtf8Bytes(root);
    }

    private static byte[] ReadSample(string fileName)
    {
        using var stream = typeof(MsgHatchJsonUnitTests).Assembly
            .GetManifestResourceStream($"MsgHatch.Domain.Tests.Unit.Samples.{fileName}")!;
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    private sealed record UnregisteredTrace : RouteTrace;
}
