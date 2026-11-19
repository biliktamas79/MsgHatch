using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Management;
using MsgHatch.Domain.Outbox;
using MsgHatch.Domain.Routing;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using NSubstitute;

namespace MsgHatch.Domain.Tests.Unit.Contracts;

/// <summary>
/// The use cases of <see cref="IRegistryCommands"/>: the health check of the transport of one channel, which records only a
/// new state of the channel (D283), and the save and the reset of the stored settings (D284). The registry is the one of
/// <see cref="RegistryTestHost"/>, and the host has a transport of the channel Test only. Each implementation gets them with
/// one subclass that only builds it.
/// </summary>
/// <typeparam name="T">The implementation.</typeparam>
public abstract class RegistryCommandsTestsBase<T>
    where T : class, IRegistryCommands
{
    private static readonly Error _unavailable = new(Errors.Unavailable, "The provider is not available.");

    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task CheckChannelAsync_NoErrorOfAnUpChannel_AddsNoRowAndGivesNoChange()
    {
        // Arrange
        await using var host = await StartWithACheckAsync(error: null);

        // Act
        var check = await host.Service(TenantKey.None).CheckChannelAsync(TestChannel.Id, Ct);

        // Assert
        Assert.Equal((TestChannel.Id, true, false, ""), (check.Value.Channel, check.Value.Healthy, check.Value.Changed, await RowsAsync(host)));
    }

    [Fact]
    public async Task CheckChannelAsync_NoErrorOfAChannelDownForErrors_AddsAnUpRowAndGivesAChange()
    {
        // Arrange
        await using var host = await StartWithACheckAsync(error: null);
        await Store(host).SetChannelDownAsync(ADown(ChannelDownReason.Errors), Ct);

        // Act
        var check = await host.Service(TenantKey.None).CheckChannelAsync(TestChannel.Id, Ct);

        // Assert
        Assert.Equal((true, true, "1 Down Errors,2 Up"), (check.Value.Healthy, check.Value.Changed, await RowsAsync(host)));
    }

    [Fact]
    public async Task CheckChannelAsync_ErrorOfAnUpChannel_AddsADownRowWithTheErrorOfTheCheckAndTheNextCheckTime()
    {
        // Arrange
        await using var host = await StartWithACheckAsync(_unavailable);

        // Act
        var check = await host.Service(TenantKey.None).CheckChannelAsync(TestChannel.Id, Ct);

        // Assert
        var down = Assert.Single((await Store(host).GetDownChannelsAsync(Ct)).Value);
        Assert.Equal(
            ((int?)503, "Unavailable", false, true),
            (check.Value.ErrorCode, check.Value.ErrorName, check.Value.Healthy, check.Value.Changed));
        Assert.Equal(
            new ChannelDown(TestChannel.Id, ChannelDownReason.Errors, NowUtc, NowUtc.AddSeconds(30), 503, "Unavailable", Note: null),
            down);
    }

    [Fact]
    public async Task CheckChannelAsync_ErrorOfAChannelDownForErrors_AddsNoRowAndGivesNoChange()
    {
        // Arrange
        await using var host = await StartWithACheckAsync(_unavailable);
        await Store(host).SetChannelDownAsync(ADown(ChannelDownReason.Errors), Ct);

        // Act
        var check = await host.Service(TenantKey.None).CheckChannelAsync(TestChannel.Id, Ct);

        // Assert
        Assert.Equal((false, false, "1 Down Errors"), (check.Value.Healthy, check.Value.Changed, await RowsAsync(host)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CheckChannelAsync_PausedChannel_AddsNoRowAndGivesNoChange(bool healthy)
    {
        // Arrange
        await using var host = await StartWithACheckAsync(healthy ? null : _unavailable);
        await Store(host).SetChannelDownAsync(ADown(ChannelDownReason.Operator), Ct);

        // Act
        var check = await host.Service(TenantKey.None).CheckChannelAsync(TestChannel.Id, Ct);

        // Assert
        Assert.Equal((healthy, false, "1 Down Operator"), (check.Value.Healthy, check.Value.Changed, await RowsAsync(host)));
    }

    [Fact]
    public async Task CheckChannelAsync_ChannelThatIsNotRegistered_GivesNotFound()
    {
        // Arrange
        await using var host = await StartWithACheckAsync(error: null);

        // Act
        var check = await host.Service(TenantKey.None).CheckChannelAsync(new ChannelId("Unknown"), Ct);

        // Assert
        Assert.Equal(Errors.NotFound.Code, check.Error?.Type.Code);
    }

    [Fact]
    public async Task CheckChannelAsync_RegisteredChannelWithNoTransportInTheHost_GivesNotFoundAndAddsNoRow()
    {
        // Arrange
        await using var host = await StartWithACheckAsync(_unavailable);

        // Act
        var check = await host.Service(TenantKey.None).CheckChannelAsync(TestChannel.SecondId, Ct);

        // Assert
        Assert.Equal((Errors.NotFound.Code, ""), (check.Error?.Type.Code, await RowsAsync(host)));
    }

    [Fact]
    public async Task CheckChannelAsync_CancelledCheck_GivesOperationCancelledAndAddsNoRow()
    {
        // Arrange
        await using var host = await StartWithACheckAsync(new Error(Errors.OperationCancelled, "The check was cancelled."));

        // Act
        var check = await host.Service(TenantKey.None).CheckChannelAsync(TestChannel.Id, Ct);

        // Assert
        Assert.Equal((Errors.OperationCancelled.Code, ""), (check.Error?.Type.Code, await RowsAsync(host)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CheckChannelAsync_StoreError_GivesTheErrorOfTheStore(bool healthy)
    {
        // Arrange
        var store = Substitute.For<IOutboxDispatchStore>();
        var storeError = ValueTask.FromResult(ErrorOr<bool>.ForError(new Error(Errors.Unavailable, "The outbox store is not available.")));
        store.SetChannelUpAsync(default, default, default, Ct).ReturnsForAnyArgs(storeError);
        store.SetChannelDownAsync(default!, Ct).ReturnsForAnyArgs(storeError);
        await using var host = await StartWithACheckAsync(healthy ? null : new Error(Errors.RateLimited, "Too many checks."), services => services.AddSingleton(store));

        // Act
        var check = await host.Service(TenantKey.None).CheckChannelAsync(TestChannel.Id, Ct);

        // Assert
        Assert.Equal(Errors.Unavailable.Code, check.Error?.Type.Code);
    }

    [Fact]
    public async Task CheckChannelAsync_NewUpRow_NotifiesTheDispatchSignalOfTheChannel()
    {
        // Arrange
        var signal = ASignal();
        await using var host = await StartWithACheckAsync(error: null, services => services.AddSingleton(signal));
        await Store(host).SetChannelDownAsync(ADown(ChannelDownReason.Errors), Ct);

        // Act
        await host.Service(TenantKey.None).CheckChannelAsync(TestChannel.Id, Ct);

        // Assert
        await signal.Received(1).NotifyAsync(DispatchTopic.ForChannel(TestChannel.Id), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CheckChannelAsync_NoNewRow_DoesNotNotifyTheDispatchSignal()
    {
        // Arrange
        var signal = ASignal();
        await using var host = await StartWithACheckAsync(error: null, services => services.AddSingleton(signal));

        // Act
        await host.Service(TenantKey.None).CheckChannelAsync(TestChannel.Id, Ct);

        // Assert
        await signal.DidNotReceiveWithAnyArgs().NotifyAsync(default, Ct);
    }

    [Fact]
    public async Task SaveChannelAsync_NewEntry_GivesTheRevision1AndTheQueriesGiveTheStoredValuesAndTheEntry()
    {
        // Arrange
        await using var host = await StartWithAWriterAsync();

        // Act
        var saved = await host.Service(TenantKey.None).SaveChannelAsync(RegistryTestHost.AChannelEntry(enabled: false), Ct);

        // Assert
        var channel = (await Queries(host).GetChannelsAsync(Ct)).Value[0];
        Assert.Equal(
            (1, "Shop channel", "Stored description", false, 1, (ChannelEntry?)saved.Value),
            (saved.Value.Revision, channel.Name, channel.Description, channel.Enabled, channel.Revision, channel.Entry));
    }

    [Fact]
    public async Task SaveMessageTypeAsync_NewEntry_GivesTheRevision1AndTheQueriesGiveTheStoredValuesAndTheEntry()
    {
        // Arrange
        await using var host = await StartWithAWriterAsync();
        var entry = RegistryTestHost.AMessageTypeEntry(disabledChannels: [TestChannel.ThirdId], enabled: false);

        // Act
        var saved = await host.Service(TenantKey.None).SaveMessageTypeAsync(entry, Ct);

        // Assert
        var type = (await Queries(host).GetMessageTypesAsync(Ct)).Value[0];
        Assert.Equal(
            (1, "Shop event", "Stored description", false, "Third", 1, "Third", 1),
            (saved.Value.Revision, type.DisplayName, type.Description, type.Enabled, NamesOf(type.DisabledChannels), type.Revision,
                NamesOf(type.Entry!.DisabledChannels), type.Entry.Revision));
    }

    [Fact]
    public async Task SaveChannelAsync_ExpectedRevisionThatIsNotTheStoredRevision_GivesConflict()
    {
        // Arrange
        await using var host = await StartWithAWriterAsync();
        await host.Service(TenantKey.None).SaveChannelAsync(RegistryTestHost.AChannelEntry(), Ct);

        // Act
        var saved = await host.Service(TenantKey.None).SaveChannelAsync(RegistryTestHost.AChannelEntry(displayName: "Second name"), Ct);

        // Assert
        Assert.Equal(Errors.Conflict.Code, saved.Error?.Type.Code);
    }

    [Fact]
    public async Task Save_ChannelOrMessageTypeThatIsNotRegistered_GivesNotFound()
    {
        // Arrange
        await using var host = await StartWithAWriterAsync();

        // Act
        var channel = await host.Service(TenantKey.None).SaveChannelAsync(RegistryTestHost.AChannelEntry(channel: new ChannelId("Unknown")), Ct);
        var type = await host.Service(TenantKey.None).SaveMessageTypeAsync(RegistryTestHost.AMessageTypeEntry(type: new MessageType("Unknown")), Ct);

        // Assert
        Assert.Equal((Errors.NotFound.Code, Errors.NotFound.Code), (channel.Error?.Type.Code, type.Error?.Type.Code));
    }

    [Fact]
    public async Task SaveMessageTypeAsync_DisabledChannelThatIsNotAChannelOfTheMessageType_GivesValidationFailed()
    {
        // Arrange
        await using var host = await StartWithAWriterAsync();

        // Act
        var saved = await host.Service(TenantKey.None).SaveMessageTypeAsync(
            RegistryTestHost.AMessageTypeEntry(disabledChannels: [TestChannel.SecondId]), Ct);

        // Assert
        Assert.Equal(Errors.ValidationFailed.Code, saved.Error?.Type.Code);
    }

    [Fact]
    public async Task Save_EntryThatBreaksARule_GivesValidationFailedAndCallsNoWriter()
    {
        // Arrange
        var writer = Substitute.For<IRegistryWriter>();
        await using var host = await StartAsync(services => RegistryTestHost.AddRegistry(services).Services.AddSingleton(writer));

        // Act
        var channel = await host.Service(TenantKey.None).SaveChannelAsync(RegistryTestHost.AChannelEntry(displayName: " "), Ct);
        var type = await host.Service(TenantKey.None).SaveMessageTypeAsync(
            RegistryTestHost.AMessageTypeEntry(disabledChannels: [TestChannel.ThirdId, TestChannel.ThirdId]), Ct);

        // Assert
        Assert.Equal(
            (Errors.ValidationFailed.Code, Errors.ValidationFailed.Code, 0),
            (channel.Error?.Type.Code, type.Error?.Type.Code, writer.ReceivedCalls().Count()));
    }

    [Fact]
    public async Task Reset_StoredEntries_DeletesThemAndTheQueriesGiveTheValuesOfTheCodeTheRevision0AndNoEntry()
    {
        // Arrange
        await using var host = await StartWithAWriterAsync();
        await host.Service(TenantKey.None).SaveChannelAsync(RegistryTestHost.AChannelEntry(enabled: false), Ct);
        await host.Service(TenantKey.None).SaveMessageTypeAsync(RegistryTestHost.AMessageTypeEntry(enabled: false), Ct);

        // Act
        var channelError = await host.Service(TenantKey.None).ResetChannelAsync(TestChannel.Id, Ct);
        var typeError = await host.Service(TenantKey.None).ResetMessageTypeAsync(RegistryTestHost.TestEventType, Ct);

        // Assert
        var channel = (await Queries(host).GetChannelsAsync(Ct)).Value[0];
        var type = (await Queries(host).GetMessageTypesAsync(Ct)).Value[0];
        Assert.Equal(
            ((Error?)null, (Error?)null, "Test channel", (string?)null, true, 0, (ChannelEntry?)null),
            (channelError, typeError, channel.Name, channel.Description, channel.Enabled, channel.Revision, channel.Entry));
        Assert.Equal(
            ((string?)null, (string?)null, true, 0, (MessageTypeEntry?)null),
            (type.DisplayName, type.Description, type.Enabled, type.Revision, type.Entry));
    }

    [Theory]
    [MemberData(nameof(RegistryTestHost.EntryCommandRows), MemberType = typeof(RegistryTestHost))]
    public async Task EntryCommand_HostWithNoWriter_GivesNotFoundWithTheMessageOfTheInProcessCommand(string command)
    {
        // Arrange
        await using var host = await StartAsync(static services => RegistryTestHost.AddRegistry(services));
        var inProcess = await RegistryTestHost.RunEntryCommandAsync(host.Server.GetRequiredKeyedService<IRegistryCommands>(TenantKey.None), command, Ct);

        // Act
        var error = await RegistryTestHost.RunEntryCommandAsync(host.Service(TenantKey.None), command, Ct);

        // Assert
        Assert.Equal((Errors.NotFound.Code, inProcess?.Message), (error?.Type.Code, error?.Message));
    }

    [Theory]
    [MemberData(nameof(RegistryTestHost.EntryCommandRows), MemberType = typeof(RegistryTestHost))]
    public async Task EntryCommand_WriterError_GivesTheErrorOfTheWriter(string command)
    {
        // Arrange
        var writer = AWriterThatFails(new Error(Errors.Unavailable, "The registry settings store is not available."));
        await using var host = await StartAsync(services => RegistryTestHost.AddRegistry(services).Services.AddSingleton(writer));

        // Act
        var error = await RegistryTestHost.RunEntryCommandAsync(host.Service(TenantKey.None), command, Ct);

        // Assert
        Assert.Equal(Errors.Unavailable.Code, error?.Type.Code);
    }

    /// <summary>Starts a host of the implementation, whose server has the services of a test.</summary>
    /// <param name="addServerServices">Adds the registry of the test, after the configuration, the clock, and the logging of the host.</param>
    /// <returns>The started host.</returns>
    protected abstract ValueTask<IRegistryQueriesTestHost<T>> StartAsync(Action<IServiceCollection> addServerServices);

    private static DateTime NowUtc => TestHost.Start.UtcDateTime;

    /// <summary>Starts a host whose transport of the channel Test gives the error, or no error, for each health check.</summary>
    private ValueTask<IRegistryQueriesTestHost<T>> StartWithACheckAsync(Error? error, Action<IServiceCollection>? addServices = null) =>
        StartAsync(services =>
        {
            var transport = Substitute.For<ITransport>();
            transport.Channel.Returns(TestChannel.Id);
            transport.CheckHealthAsync(Ct).ReturnsForAnyArgs(_ => ValueTask.FromResult(error));
            RegistryTestHost.AddRegistry(services).UseTransport(_ => transport).Services.AddSingleton<TimeProvider>(TestHost.NewFakeTime());
            addServices?.Invoke(services);
        });

    /// <summary>Starts a host with the stored registry and the in-memory registry settings store, so with a registry writer.</summary>
    private ValueTask<IRegistryQueriesTestHost<T>> StartWithAWriterAsync() =>
        StartAsync(static services => RegistryTestHost.AddRegistry(services).UseStoredRegistry());

    private static IRegistryQueries Queries(IRegistryQueriesTestHost<T> host) => host.Server.GetRequiredKeyedService<IRegistryQueries>(TenantKey.None);

    private static string NamesOf(IReadOnlyList<ChannelId> channels) => string.Join(',', channels.Select(static channel => channel.Name));

    private static IRegistryWriter AWriterThatFails(Error error)
    {
        var writer = Substitute.For<IRegistryWriter>();
        writer.SaveChannelAsync(default!, default).ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<ChannelEntry>.ForError(error)));
        writer.SaveMessageTypeAsync(default!, default).ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<MessageTypeEntry>.ForError(error)));
        writer.ResetChannelAsync(default, default).ReturnsForAnyArgs(ValueTask.FromResult<Error?>(error));
        writer.ResetMessageTypeAsync(default, default).ReturnsForAnyArgs(ValueTask.FromResult<Error?>(error));
        return writer;
    }

    private static IDispatchSignal ASignal()
    {
        var signal = Substitute.For<IDispatchSignal>();
        signal.NotifyAsync(default, default).ReturnsForAnyArgs(ValueTask.FromResult<Error?>(null));
        return signal;
    }

    private static IOutboxDispatchStore Store(IRegistryQueriesTestHost<T> host) => host.Server.GetRequiredService<IOutboxDispatchStore>();

    /// <summary>The state rows of the channel Test, oldest first, as text: the version, the kind, and the reason of a down row.</summary>
    private static async Task<string> RowsAsync(IRegistryQueriesTestHost<T> host)
    {
        var rows = await Store(host).GetChannelStatesAsync(DateTime.MinValue, Ct);
        return string.Join(',', rows.Value.Where(static row => row.Channel == TestChannel.Id).Reverse().Select(static row => $"{row.Version} {row.Kind}{(row.Reason is { } reason ? $" {reason}" : "")}"));
    }

    private static ChannelDown ADown(ChannelDownReason reason) =>
        reason == ChannelDownReason.Errors
            ? new ChannelDown(TestChannel.Id, reason, NowUtc.AddMinutes(-1), NowUtc.AddSeconds(30), 500, "Exception", Note: null)
            : new ChannelDown(TestChannel.Id, reason, NowUtc.AddMinutes(-1), NextCheckAtUtc: null, ErrorCode: null, ErrorName: null, "maintenance");
}
