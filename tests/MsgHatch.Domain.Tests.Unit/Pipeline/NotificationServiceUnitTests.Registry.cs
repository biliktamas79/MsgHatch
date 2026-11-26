using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Pipeline;
using MsgHatch.Domain.Routing;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using NSubstitute;
using static MsgHatch.Domain.Tests.Unit.TestTypes.TestRoutes;

namespace MsgHatch.Domain.Tests.Unit.Pipeline;

/// <summary>The registry settings in the steps 2 and 3 of the pipeline (section 4.8, D118, D168, D169).</summary>
public sealed partial class NotificationServiceUnitTests
{
    private static readonly MessageType _testEventType = new("TestEvent");

    [Fact]
    public async Task ProcessAsync_DisabledMessageType_StoresNothingAndCallsNoRouter()
    {
        // Arrange
        _doubles.Routes = [ARoute("u1"), ARoute("u2")];
        await using var provider = _doubles.Build(_time, more: b => b.UseRegistrySettingsStore(_ => StoreWith(ADisabledMessageType())).UseStoredRegistry());

        // Act
        var result = await ProcessAsync(provider, AContext());

        // Assert
        Assert.Equal((0, 0, 0, 0), (result.Value.Stored, result.Value.Failed, result.Value.Errors.Count, result.Value.Skipped));
        Assert.Empty(_doubles.Stored);
        _ = _doubles.Router(TenantKey.None).DidNotReceiveWithAnyArgs().RouteAsync(default!, Ct);
    }

    [Fact]
    public async Task ProcessAsync_DisabledMessageType_LogsAnInformationRecord()
    {
        // Arrange
        await using var provider = _doubles.Build(
            _time, enableLogging: false, more: b => b.UseRegistrySettingsStore(_ => StoreWith(ADisabledMessageType())).UseStoredRegistry());
        var context = AContext(_tenant, MessageFlags.Test);

        // Act
        await ProcessAsync(provider, context);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal(
            (LogLevel.Information, 1011, context.MessageId.ToString(), "t1", "Test"),
            (record.Level, record.Id.Id, record.GetStructuredStateValue("MessageId"), record.GetStructuredStateValue("Tenant"),
                record.GetStructuredStateValue("MessageFlags")));
    }

    [Fact]
    public async Task ProcessAsync_RouteOnADisabledChannel_SkipsAndCountsItAndStoresTheOtherRoutes()
    {
        // Arrange
        _doubles.Routes = [ARoute("u1"), ARoute("u2", channel: TestChannel.SecondId, settings: new OtherTestChannelSettings()), ARoute("u3")];
        var disabled = new ChannelEntry(TestChannel.SecondId, null, null, Enabled: false, Revision: 1, TestHost.Start.UtcDateTime);
        await using var provider = _doubles.Build(_time, more: b => b.UseRegistrySettingsStore(_ => StoreWith(disabled)).UseStoredRegistry());

        // Act
        var result = await ProcessAsync(provider, AContext());

        // Assert
        Assert.Equal((2, 0, 1), (result.Value.Stored, result.Value.Failed, result.Value.Skipped));
        Assert.Equal(["u1", "u3"], _doubles.Stored.Select(static message => message.Target.Id));
    }

    [Fact]
    public async Task ProcessAsync_RouteOnADisabledChannelOfTheMessageType_SkipsAndCountsItAndStoresTheOtherRoutes()
    {
        // Arrange
        _doubles.Routes = [ARoute("u1", channel: TestChannel.SecondId, settings: new OtherTestChannelSettings()), ARoute("u2"), ARoute("u3")];
        await using var provider = _doubles.Build(
            _time, more: b => b.UseRegistrySettingsStore(_ => StoreWith(ADisabledMessageType() with { Enabled = true, DisabledChannels = [TestChannel.Id] })).UseStoredRegistry());

        // Act
        var result = await ProcessAsync(provider, AContext());

        // Assert
        Assert.Equal((1, 0, 2), (result.Value.Stored, result.Value.Failed, result.Value.Skipped));
        Assert.Equal(TestChannel.SecondId, Assert.Single(_doubles.Stored).Channel);
    }

    [Fact]
    public async Task ProcessAsync_SkippedRoute_LogsADebugRecordWithTheChannel()
    {
        // Arrange
        _doubles.Routes = [ARoute("u1")];
        var disabled = new ChannelEntry(TestChannel.Id, null, null, Enabled: false, Revision: 1, TestHost.Start.UtcDateTime);
        await using var provider = _doubles.Build(_time, enableLogging: false, more: b => b.UseRegistrySettingsStore(_ => StoreWith(disabled)).UseStoredRegistry());

        // Act
        await ProcessAsync(provider, AContext(_tenant));

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal(
            (LogLevel.Debug, 1012, "Test", "t1"),
            (record.Level, record.Id.Id, record.GetStructuredStateValue("Channel"), record.GetStructuredStateValue("Tenant")));
    }

    [Fact]
    public async Task ProcessAsync_FirstRegistryReadThatHasNotEnded_WaitsForItAndThenAppliesTheSettings()
    {
        // Arrange
        _doubles.Routes = [ARoute("u1")];
        var read = new TaskCompletionSource<ErrorOr<RegistryEntries>>();
        var store = Substitute.For<IRegistrySettingsStore>();
        store.GetEntriesAsync(Ct).ReturnsForAnyArgs(new ValueTask<ErrorOr<RegistryEntries>>(read.Task));
        await using var provider = _doubles.Build(_time, more: b => b.UseRegistrySettingsStore(_ => store).UseStoredRegistry());
        var processing = ProcessAsync(provider, AContext());
        var waited = !processing.IsCompleted;

        // Act
        read.SetResult(new RegistryEntries([], [ADisabledMessageType()]));
        var result = await processing;

        // Assert
        Assert.Equal((true, 0), (waited, result.Value.Stored));
        _ = _doubles.Router(TenantKey.None).DidNotReceiveWithAnyArgs().RouteAsync(default!, Ct);
    }

    [Fact]
    public async Task ProcessAsync_FirstRegistryReadThatFailed_DoesNotWaitAndUsesTheValuesOfTheCode()
    {
        // Arrange
        _doubles.Routes = [ARoute("u1"), ARoute("u2")];
        await using var provider = _doubles.Build(
            _time, more: b => b.UseRegistrySettingsStore(_ => RegistryTestHost.AStoreThatFails(new Error(Errors.Unavailable))).UseStoredRegistry());

        // Act
        var result = await ProcessAsync(provider, AContext()).WaitAsync(TestHost.HangGuard, TimeProvider.System, Ct);

        // Assert
        Assert.Equal((2, 0), (result.Value.Stored, result.Value.Skipped));
    }

    [Fact]
    public async Task ProcessAsync_CancelledWhileTheFirstRegistryReadRuns_GivesOperationCancelled()
    {
        // Arrange
        var store = Substitute.For<IRegistrySettingsStore>();
        store.GetEntriesAsync(Ct).ReturnsForAnyArgs(new ValueTask<ErrorOr<RegistryEntries>>(new TaskCompletionSource<ErrorOr<RegistryEntries>>().Task));
        await using var provider = _doubles.Build(_time, more: b => b.UseRegistrySettingsStore(_ => store).UseStoredRegistry());
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var processing = ProcessAsync(provider, AContext(), cancellation.Token);

        // Act
        await cancellation.CancelAsync();
        var result = await processing;

        // Assert
        Assert.Equal(Errors.OperationCancelled, result.Error?.Type);
    }

    [Fact]
    public async Task PreviewAsync_DisabledMessageType_GivesTheRoutesOfTheCode()
    {
        // Arrange
        _doubles.Routes = [ARoute("u1"), ARoute("u2")];
        await using var provider = _doubles.Build(_time, more: b => b.UseRegistrySettingsStore(_ => StoreWith(ADisabledMessageType())).UseStoredRegistry());

        // Act
        var results = await PreviewAsync(provider);

        // Assert
        Assert.Equal(["u1", "u2"], results.Value.Select(static result => result.Message!.Target.Id));
    }

    [Fact]
    public async Task PreviewAsync_RouteOnADisabledChannel_GivesItsMessage()
    {
        // Arrange
        _doubles.Routes = [ARoute("u1"), ARoute("u2", channel: TestChannel.SecondId, settings: new OtherTestChannelSettings())];
        var disabled = new ChannelEntry(TestChannel.SecondId, null, null, Enabled: false, Revision: 1, TestHost.Start.UtcDateTime);
        await using var provider = _doubles.Build(_time, more: b => b.UseRegistrySettingsStore(_ => StoreWith(disabled)).UseStoredRegistry());

        // Act
        var results = await PreviewAsync(provider);

        // Assert
        Assert.Equal([TestChannel.Id, TestChannel.SecondId], results.Value.Select(static result => result.Message!.Channel));
    }

    private static MessageTypeEntry ADisabledMessageType() =>
        new(_testEventType, null, null, Enabled: false, [], Revision: 1, TestHost.Start.UtcDateTime);

    private static InMemoryRegistrySettingsStore StoreWith(MessageTypeEntry entry)
    {
        var store = new InMemoryRegistrySettingsStore();
        _ = store.SaveMessageTypeEntryAsync(entry, Ct);
        return store;
    }

    private static InMemoryRegistrySettingsStore StoreWith(ChannelEntry entry)
    {
        var store = new InMemoryRegistrySettingsStore();
        _ = store.SaveChannelEntryAsync(entry, Ct);
        return store;
    }

    private static async Task<ErrorOr<IReadOnlyList<RouteResult>>> PreviewAsync(IServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<INotificationService>().PreviewAsync(new TestEvent("text"), AContext(), routes: null, Ct);
    }
}
