using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Outbox;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using NSubstitute;
using static MsgHatch.Domain.Tests.Unit.TestTypes.OutboxDispatchDoubles;

namespace MsgHatch.Domain.Tests.Unit.Dispatch;

/// <summary>The test delivery of a channel (D83, section 4.8, publishing step 3).</summary>
public sealed partial class OutboxDispatcherUnitTests
{
    private const string _redirectTo = "tester@example.com";

    [Fact]
    public async Task DispatchPendingAsync_TestMessageWithTheDefaultTestDelivery_RecordsASuppressedTryAndSendsNothing()
    {
        // Arrange
        _doubles.ClaimsGive(TestChannel.Id, Claim(AMessage(flags: MessageFlags.Test)));
        var transport = TestTransports.For(TestChannel.Id);
        await using var provider = _doubles.Build(transports: transport);

        // Act
        var summary = await provider.GetRequiredService<IOutboxDispatcher>().DispatchPendingAsync(10, Ct);

        // Assert
        Assert.Equal((PublishOutcome.Suppressed, 1), ((await _doubles.NextRecordAsync()).Try.Outcome, summary.Value.Succeeded));
        await transport.DidNotReceiveWithAnyArgs().SendAsync(default!, default, Ct);
    }

    [Fact]
    public async Task DispatchPendingAsync_TestMessageWithTheModeRedirect_SendsToTheRedirectAddress()
    {
        // Arrange
        _doubles.ClaimsGive(TestChannel.Id, Claim(AMessage(flags: MessageFlags.Test)));
        var transport = TestTransports.For(TestChannel.Id);
        await using var provider = _doubles.Build(
            more: static b => b.ConfigureTestDelivery(TestChannel.Id, static o => (o.Mode, o.RedirectTo) = (TestDeliveryMode.Redirect, _redirectTo)),
            transports: transport);

        // Act
        await provider.GetRequiredService<IOutboxDispatcher>().DispatchPendingAsync(10, Ct);

        // Assert
        await transport.Received(1).SendAsync(Arg.Any<OutgoingMessage>(), _redirectTo, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchPendingAsync_TestMessageWithTheModeDeliver_SendsWithNoRedirectAddress()
    {
        // Arrange
        _doubles.ClaimsGive(TestChannel.Id, Claim(AMessage(flags: MessageFlags.Test)));
        var transport = TestTransports.For(TestChannel.Id);
        await using var provider = _doubles.Build(
            more: static b => b.ConfigureTestDelivery(TestChannel.Id, static o => (o.Mode, o.RedirectTo) = (TestDeliveryMode.Deliver, _redirectTo)),
            transports: transport);

        // Act
        await provider.GetRequiredService<IOutboxDispatcher>().DispatchPendingAsync(10, Ct);

        // Assert
        await transport.Received(1).SendAsync(Arg.Any<OutgoingMessage>(), null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchPendingAsync_MessageWithNoTestFlagWhileTheTestDeliveryIsSuppress_SendsItNormally()
    {
        // Arrange
        _doubles.ClaimsGive(TestChannel.Id, Claim(AMessage()));
        var transport = TestTransports.For(TestChannel.Id);
        await using var provider = _doubles.Build(transports: transport);

        // Act
        await provider.GetRequiredService<IOutboxDispatcher>().DispatchPendingAsync(10, Ct);

        // Assert
        Assert.Equal(PublishOutcome.Succeeded, (await _doubles.NextRecordAsync()).Try.Outcome);
        await transport.Received(1).SendAsync(Arg.Any<OutgoingMessage>(), null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchPendingAsync_SuppressedTryBetweenTransientErrors_DoesNotChangeTheCount()
    {
        // Arrange
        _doubles.ClaimsGive(TestChannel.Id, Claim(AMessage(), AMessage(), AMessage(flags: MessageFlags.Test), AMessage()));
        await using var provider = BuildWithTheDownOptions(TestTransports.For(TestChannel.Id, new Error(Errors.Unavailable)));

        // Act
        await provider.GetRequiredService<IOutboxDispatcher>().DispatchPendingAsync(10, Ct);

        // Assert
        var down = Assert.Single(SetDownStates());
        Assert.Equal(503, down.ErrorCode); // the fourth try, not the suppressed third try, reaches the threshold
        Assert.Equal(PublishOutcome.TransientError, RecordedTries(_doubles.Store)[^1].Outcome);
    }
}
