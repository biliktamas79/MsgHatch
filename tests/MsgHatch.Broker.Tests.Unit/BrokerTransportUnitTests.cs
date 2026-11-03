using MsgHatch.Broker.Tests.Unit.TestTypes;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Tests.Unit.Contracts;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using NSubstitute;

namespace MsgHatch.Broker.Tests.Unit;

/// <summary>
/// The broker transport: the use cases of the transport contract, and the rules that only this transport has: the
/// headers that it builds for the broker client, and the values of the outgoing message that it gives to the client
/// (section 4.6, section 4.8).
/// </summary>
public sealed class BrokerTransportUnitTests : TransportTestsBase<BrokerTransport>
{
    private static readonly Guid _id = new("0b6f9a40-6a3e-4c55-9a5c-0d7e3c1b2a11");

    private readonly IBrokerClient _client;

    public BrokerTransportUnitTests()
        : this(new BrokerTransportTestDriver())
    {
    }

    private BrokerTransportUnitTests(BrokerTransportTestDriver driver)
        : base(new BrokerTransport(driver.Client), driver) => _client = driver.Client;

    public static TheoryData<string, string?, MessageFlags, string?, Dictionary<string, string>?, string[]> HeaderRows => new()
    {
        { "no tenant, no settings headers", null, MessageFlags.None, null, null, [$"msghatch-id={_id}"] },
        { "the headers of the settings", null, MessageFlags.None, null, new() { ["x-source"] = "orders" }, [$"msghatch-id={_id}", "x-source=orders"] },
        { "a tenant", "t1", MessageFlags.None, null, null, [$"msghatch-id={_id}", "msghatch-tenant=t1"] },
        { "a test message", null, MessageFlags.Test, null, null, [$"msghatch-id={_id}", "msghatch-test=true"] },
        { "a redirect of a test message", null, MessageFlags.Test, "testers", null, [$"msghatch-id={_id}", "msghatch-redirect=testers", "msghatch-test=true"] },
        { "a settings header with the name of the library", null, MessageFlags.None, null, new() { ["msghatch-id"] = "other" }, [$"msghatch-id={_id}"] },
    };

    [Theory]
    [MemberData(nameof(HeaderRows))]
    public async Task SendAsync_OutgoingMessage_GivesTheHeadersOfTheRowToTheClient(
        string row, string? tenant, MessageFlags flags, string? redirectTo, Dictionary<string, string>? settingsHeaders, string[] expected)
    {
        // Arrange
        var message = BrokerMessages.AnOutgoingMessage(_id, tenant is null ? null : new TenantId(tenant), flags, settingsHeaders);

        // Act
        await Transport.SendAsync(message, redirectTo, Ct);

        // Assert
        Assert.Equal((row, string.Join(' ', expected)), (row, string.Join(' ', PublishedHeaders())));
    }

    [Fact]
    public async Task SendAsync_OutgoingMessageOfATenant_GivesTheIdTheTenantAndThePayloadToTheClient()
    {
        // Arrange
        var message = BrokerMessages.AnOutgoingMessage(_id, new TenantId("t1"));

        // Act
        await Transport.SendAsync(message, redirectTo: null, Ct);

        // Assert
        var publish = Published();
        Assert.Equal((_id, (TenantId?)new TenantId("t1")), (publish.MessageId, publish.Tenant));
        Assert.Same(message.Envelope.Payload, publish.Payload);
    }

    [Fact]
    public async Task SendAsync_TwoPayloadTypes_GivesEachPayloadToTheClient()
    {
        // Arrange
        var (first, second) = (BrokerMessages.AnOutgoingMessage(), BrokerMessages.AnOutgoingMessage(new OtherPayload(7)));

        // Act
        await Transport.SendAsync(first, redirectTo: null, Ct);
        await Transport.SendAsync(second, redirectTo: null, Ct);

        // Assert
        Assert.Equal(
            [first.Envelope.Payload, second.Envelope.Payload],
            _client.ReceivedCalls().Select(static call => ((BrokerPublish)call.GetArguments()[0]!).Payload));
    }

    [Fact]
    public async Task SendAsync_ContentThatIsNotABrokerEnvelope_ReturnsValidationFailedAndCallsNoClient()
    {
        // Arrange
        var message = TestMessages.AnOutgoingMessage(channel: BrokerChannel.Id);

        // Act
        var result = await Transport.SendAsync(message, redirectTo: null, Ct);

        // Assert
        Assert.Equal(Errors.ValidationFailed, result.Error?.Type);
        Assert.Empty(_client.ReceivedCalls());
    }

    [Fact]
    public async Task CheckHealthAsync_ErrorOfTheClient_ReturnsTheSameError()
    {
        // Arrange
        var expected = new Error(Errors.Unavailable);
        _client.CheckHealthAsync(Ct).Returns(ValueTask.FromResult<Error?>(expected));

        // Act
        var error = await Transport.CheckHealthAsync(Ct);

        // Assert
        Assert.Same(expected, error);
    }

    [Fact]
    public void Channel_BrokerTransport_IsTheBrokerChannel()
    {
        // Act
        var channel = Transport.Channel;

        // Assert
        Assert.Equal(BrokerChannel.Id, channel);
    }

    private BrokerPublish Published() => (BrokerPublish)_client.ReceivedCalls().Single().GetArguments()[0]!;

    private string[] PublishedHeaders() =>
        [.. Published().Headers.Select(static header => $"{header.Key}={header.Value}").Order(StringComparer.Ordinal)];
}
