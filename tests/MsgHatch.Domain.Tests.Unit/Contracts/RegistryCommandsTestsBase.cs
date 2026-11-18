using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Management;
using MsgHatch.Domain.Outbox;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using NSubstitute;

namespace MsgHatch.Domain.Tests.Unit.Contracts;

/// <summary>
/// The use cases of <see cref="IRegistryCommands"/> (D283): the health check of the transport of one channel, which records
/// only a new state of the channel. The registry is the one of <see cref="RegistryTestHost"/>, and the host has a transport
/// of the channel Test only. Each implementation gets them with one subclass that only builds it.
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
        await signal.DidNotReceiveWithAnyArgs().NotifyAsync(default, default);
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
            RegistryTestHost.AddRegistry(services).UseTransport(_ => transport);
            addServices?.Invoke(services);
        });

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
