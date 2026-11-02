using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Outbox;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using NSubstitute;
using static MsgHatch.Domain.Tests.Unit.TestTypes.OutboxDispatchDoubles;

namespace MsgHatch.Domain.Tests.Unit.Dispatch;

/// <summary>
/// The upgrade of a claimed outgoing message of an old envelope version before the send (section 4.13, D25). The
/// current version of <c>Test:TestEnvelope</c> is 2, version 1 has an upgrade, and the error threshold is 1.
/// </summary>
public sealed partial class OutboxDispatcherUnitTests
{
    [Fact]
    public async Task DispatchPendingAsync_OldEnvelopeVersion_SendsTheUpgradedEnvelopeWithTheCurrentVersion()
    {
        // Arrange
        _doubles.ClaimsGive(TestChannel.Id, Claim(AVersionOneMessage("old")));
        var transport = TestTransports.For(TestChannel.Id);
        await using var provider = BuildWithEnvelopeVersionTwo(transport);

        // Act
        await provider.GetRequiredService<IOutboxDispatcher>().DispatchPendingAsync(10, Ct);

        // Assert
        var sent = Assert.Single(SentMessages(transport));
        Assert.Equal((2, (object)new TestEnvelope("old upgraded")), (sent.ContentVersion, sent.Content));
    }

    [Fact]
    public async Task DispatchPendingAsync_OldEnvelopeVersion_RecordsTheTryOfTheStoredMessage()
    {
        // Arrange
        var stored = AVersionOneMessage("old");
        _doubles.ClaimsGive(TestChannel.Id, Claim(stored));
        await using var provider = BuildWithEnvelopeVersionTwo(TestTransports.For(TestChannel.Id));

        // Act
        await provider.GetRequiredService<IOutboxDispatcher>().DispatchPendingAsync(10, Ct);

        // Assert
        Assert.Same(stored, (await _doubles.NextRecordAsync()).Message);
    }

    [Fact]
    public async Task DispatchPendingAsync_EnvelopeVersionThatIsNewerThanTheCode_RecordsATransientUnsupportedVersionTryAndSendsNothing()
    {
        // Arrange
        _doubles.ClaimsGive(TestChannel.Id, Claim(AMessage() with { ContentVersion = 3 }));
        var transport = TestTransports.For(TestChannel.Id);
        await using var provider = BuildWithEnvelopeVersionTwo(transport);

        // Act
        var summary = await provider.GetRequiredService<IOutboxDispatcher>().DispatchPendingAsync(10, Ct);

        // Assert
        var record = await _doubles.NextRecordAsync();
        Assert.Equal(
            (PublishOutcome.TransientError, (int?)426, "UnsupportedVersion", true, 1),
            (record.Try.Outcome, record.Try.ErrorCode, record.Try.ErrorName, record.NextTryAtUtc is not null, summary.Value.Retried));
        await transport.DidNotReceiveWithAnyArgs().SendAsync(default!, default, Ct);
    }

    [Fact]
    public async Task DispatchPendingAsync_EnvelopeVersionThatIsNewerThanTheCode_DoesNotCountAsATransportErrorForTheDownState()
    {
        // Arrange
        _doubles.ClaimsGive(TestChannel.Id, Claim(AMessage() with { ContentVersion = 3 }));
        await using var provider = BuildWithEnvelopeVersionTwo(TestTransports.For(TestChannel.Id));

        // Act
        await provider.GetRequiredService<IOutboxDispatcher>().DispatchPendingAsync(10, Ct);

        // Assert
        Assert.Empty(SetDownStates());
    }

    [Fact]
    public async Task DispatchPendingAsync_UpgradeFunctionThatThrows_RecordsATransientExceptionTryAndSendsNothing()
    {
        // Arrange
        _doubles.ClaimsGive(TestChannel.Id, Claim(AVersionOneMessage("old")));
        var transport = TestTransports.For(TestChannel.Id);
        await using var provider = BuildWithEnvelopeVersionTwo(transport, static _ => throw new InvalidOperationException("defective upgrade"));

        // Act
        var summary = await provider.GetRequiredService<IOutboxDispatcher>().DispatchPendingAsync(10, Ct);

        // Assert
        var record = await _doubles.NextRecordAsync();
        Assert.Equal(
            (PublishOutcome.TransientError, (int?)500, true, 1),
            (record.Try.Outcome, record.Try.ErrorCode, record.NextTryAtUtc is not null, summary.Value.Retried));
        await transport.DidNotReceiveWithAnyArgs().SendAsync(default!, default, Ct);
    }

    [Fact]
    public async Task DispatchPendingAsync_UpgradeFunctionThatThrows_DoesNotCountAsATransportErrorForTheDownState()
    {
        // Arrange
        _doubles.ClaimsGive(TestChannel.Id, Claim(AVersionOneMessage("old")));
        await using var provider = BuildWithEnvelopeVersionTwo(
            TestTransports.For(TestChannel.Id), static _ => throw new InvalidOperationException("defective upgrade"));

        // Act
        await provider.GetRequiredService<IOutboxDispatcher>().DispatchPendingAsync(10, Ct);

        // Assert
        Assert.Empty(SetDownStates());
    }

    private static OutgoingMessage<OtherTestEnvelope> AVersionOneMessage(string text)
    {
        var current = AMessage();
        return new OutgoingMessage<OtherTestEnvelope>(
            current.SchemaVersion, current.Delivery, current.SourceMessageId, current.Tenant, current.Type, current.Channel,
            current.Target, current.ContentType, ContentVersion: 1, current.Flags, current.TraceParent, current.Trace,
            new OtherTestEnvelope(text));
    }

    private static OutgoingMessage[] SentMessages(ITransport transport) =>
        [.. transport.ReceivedCalls()
            .Where(static call => call.GetMethodInfo().Name == nameof(ITransport.SendAsync))
            .Select(static call => (OutgoingMessage)call.GetArguments()[0]!)];

    private Microsoft.Extensions.DependencyInjection.ServiceProvider BuildWithEnvelopeVersionTwo(
        ITransport transport, Func<OtherTestEnvelope, TestEnvelope>? upgrade = null) =>
        _doubles.Build(
            more: b => b
                .ConfigureChannelDown(TestChannel.Id, static o => o.ErrorThreshold = 1)
                .AddMessage("TestEvent", 2, TestJsonContext.Default.TestEvent, m => m
                    .AsChannel(
                        TestChannel.Id, "TestEnvelope", 2, TestJsonContext.Default.TestEnvelope, TestChannel.AnyComposer<TestEvent>(),
                        configure: channel => channel.WithUpgrade(
                            1, TestJsonContext.Default.OtherTestEnvelope, upgrade ?? (static old => new TestEnvelope($"{old.Text} upgraded"))))),
            transports: transport);
}
