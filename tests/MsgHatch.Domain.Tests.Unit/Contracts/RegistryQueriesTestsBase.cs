using Microsoft.Extensions.DependencyInjection;
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
/// The use cases of <see cref="IRegistryQueries"/> (D268, D269): the channels and the message types of the code with the
/// revision 0, the stored settings with their revisions, the disabled channels of a message type, each tenant key, and the
/// error of the registry writer. The registry is the one of <see cref="RegistryTestHost"/>. Each implementation gets them
/// with one subclass that only builds it.
/// </summary>
/// <typeparam name="T">The implementation.</typeparam>
public abstract class RegistryQueriesTestsBase<T>
    where T : class, IRegistryQueries
{
    private static readonly TenantKey _t1 = TenantKey.For(new TenantId("t1"));

    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task GetChannelsAsync_SetupInCode_GivesEachChannelOfTheCodeWithTheRevision0()
    {
        // Arrange
        await using var host = await StartAsync(static services => RegistryTestHost.AddRegistry(services));
        (ChannelId, string, string?, ChannelFeatures, bool, int)[] expected =
        [
            (TestChannel.Id, "Test channel", null, ChannelFeatures.None, true, 0),
            (TestChannel.SecondId, "Second test channel", null, ChannelFeatures.GroupDelivery, true, 0),
            (TestChannel.ThirdId, "Third test channel", null, ChannelFeatures.None, true, 0),
            (RegistryTestHost.DescribedChannel.Id, "Described channel", "Code description", ChannelFeatures.None, true, 0),
        ];

        // Act
        var channels = await host.Service(TenantKey.None).GetChannelsAsync(Ct);

        // Assert
        Assert.Equal(expected, channels.Value.Select(static channel => (channel.Id, channel.Name, channel.Description, channel.Features, channel.Enabled, channel.Revision)));
    }

    [Fact]
    public async Task GetMessageTypesAsync_SetupInCode_GivesEachMessageTypeWithItsChannelsAndTheRevision0()
    {
        // Arrange
        await using var host = await StartAsync(static services => RegistryTestHost.AddRegistry(services));

        // Act
        var types = await host.Service(TenantKey.None).GetMessageTypesAsync(Ct);

        // Assert
        Assert.Equal(
            ["TestEvent channels=Test,Third name= description= enabled=True disabled= revision=0",
                "OtherTestEvent channels=Test name= description= enabled=True disabled= revision=0"],
            types.Value.Select(TextOf));
    }

    [Fact]
    public async Task GetChannelsAsync_StoredEntry_GivesTheStoredNameDescriptionAndEnabledFlagWithTheRevisionOfTheEntry()
    {
        // Arrange
        await using var host = await StartAsync(static services => RegistryTestHost.AddRegistry(services).UseStoredRegistry());
        await host.Server.GetRequiredService<IRegistryWriter>().SaveChannelAsync(RegistryTestHost.AChannelEntry(enabled: false), Ct);

        // Act
        var channels = await host.Service(TenantKey.None).GetChannelsAsync(Ct);

        // Assert
        var stored = channels.Value[0];
        Assert.Equal(
            (TestChannel.Id, "Shop channel", "Stored description", false, 1, 0),
            (stored.Id, stored.Name, stored.Description, stored.Enabled, stored.Revision, channels.Value[1].Revision));
    }

    [Fact]
    public async Task GetMessageTypesAsync_StoredEntry_GivesTheStoredDisplayNameDescriptionAndEnabledFlagWithTheRevisionOfTheEntry()
    {
        // Arrange
        await using var host = await StartAsync(static services => RegistryTestHost.AddRegistry(services).UseStoredRegistry());
        await host.Server.GetRequiredService<IRegistryWriter>().SaveMessageTypeAsync(RegistryTestHost.AMessageTypeEntry(enabled: false), Ct);

        // Act
        var types = await host.Service(TenantKey.None).GetMessageTypesAsync(Ct);

        // Assert
        Assert.Equal(
            ["TestEvent channels=Test,Third name=Shop event description=Stored description enabled=False disabled= revision=1",
                "OtherTestEvent channels=Test name= description= enabled=True disabled= revision=0"],
            types.Value.Select(TextOf));
    }

    [Fact]
    public async Task GetMessageTypesAsync_DisabledChannel_GivesItInTheDisabledChannelsOfItsMessageTypeOnly()
    {
        // Arrange
        await using var host = await StartAsync(static services => RegistryTestHost.AddRegistry(services).UseStoredRegistry());
        await host.Server.GetRequiredService<IRegistryWriter>().SaveMessageTypeAsync(
            RegistryTestHost.AMessageTypeEntry(disabledChannels: [TestChannel.ThirdId], displayName: null, description: null), Ct);

        // Act
        var types = await host.Service(TenantKey.None).GetMessageTypesAsync(Ct);

        // Assert
        Assert.Equal(
            ["TestEvent channels=Test,Third name= description= enabled=True disabled=Third revision=1",
                "OtherTestEvent channels=Test name= description= enabled=True disabled= revision=0"],
            types.Value.Select(TextOf));
    }

    [Fact]
    public async Task GetChannelsAsync_StoredEntry_GivesTheEntryOfItsChannelAndNoEntryForTheOtherChannels()
    {
        // Arrange
        await using var host = await StartAsync(static services => RegistryTestHost.AddRegistry(services).UseStoredRegistry());
        var saved = await host.Server.GetRequiredService<IRegistryWriter>().SaveChannelAsync(
            RegistryTestHost.AChannelEntry(channel: TestChannel.SecondId, description: null, enabled: false), Ct);

        // Act
        var channels = await host.Service(TenantKey.None).GetChannelsAsync(Ct);

        // Assert
        ChannelEntry?[] expected = [null, saved.Value, null, null];
        Assert.Equal(expected, channels.Value.Select(static channel => channel.Entry));
    }

    [Fact]
    public async Task GetMessageTypesAsync_StoredEntry_GivesTheEntryOfItsMessageTypeAndNoEntryForTheOtherMessageType()
    {
        // Arrange
        await using var host = await StartAsync(static services => RegistryTestHost.AddRegistry(services).UseStoredRegistry());
        var saved = await host.Server.GetRequiredService<IRegistryWriter>().SaveMessageTypeAsync(
            RegistryTestHost.AMessageTypeEntry(disabledChannels: [TestChannel.ThirdId], description: null), Ct);

        // Act
        var types = await host.Service(TenantKey.None).GetMessageTypesAsync(Ct);

        // Assert
        Assert.Equal(
            [EntryTextOf(saved.Value), "no entry"],
            types.Value.Select(static type => type.Entry is { } entry ? EntryTextOf(entry) : "no entry"));
    }

    [Fact]
    public async Task Queries_NoWriter_GiveNoEntry()
    {
        // Arrange
        await using var host = await StartAsync(static services => RegistryTestHost.AddRegistry(services));

        // Act
        var channels = await host.Service(TenantKey.None).GetChannelsAsync(Ct);
        var types = await host.Service(TenantKey.None).GetMessageTypesAsync(Ct);

        // Assert
        Assert.Equal(
            (4, 2),
            (channels.Value.Count(static channel => channel.Entry is null), types.Value.Count(static type => type.Entry is null)));
    }

    [Fact]
    public async Task Queries_KeyOfATenant_GiveTheValuesOfTenantKeyNone()
    {
        // Arrange
        await using var host = await StartAsync(static services => RegistryTestHost.AddRegistry(services).UseStoredRegistry());
        await host.Server.GetRequiredService<IRegistryWriter>().SaveChannelAsync(RegistryTestHost.AChannelEntry(), Ct);
        var shared = await host.Service(TenantKey.None).GetChannelsAsync(Ct);
        var sharedTypes = await host.Service(TenantKey.None).GetMessageTypesAsync(Ct);
        string[] expected = [.. shared.Value.Select(static channel => channel.ToString()), .. sharedTypes.Value.Select(TextOf)];

        // Act
        var channels = await host.Service(_t1).GetChannelsAsync(Ct);
        var types = await host.Service(_t1).GetMessageTypesAsync(Ct);

        // Assert
        string[] actual = [.. channels.Value.Select(static channel => channel.ToString()), .. types.Value.Select(TextOf)];
        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task Queries_WriterError_GiveTheErrorOfTheWriter()
    {
        // Arrange
        var writer = Substitute.For<IRegistryWriter>();
        writer.GetEntriesAsync(Ct).ReturnsForAnyArgs(
            ValueTask.FromResult(ErrorOr<RegistryEntries>.ForError(new Error(Errors.Unavailable, "The registry settings store is not available."))));
        await using var host = await StartAsync(services => RegistryTestHost.AddRegistry(services).Services.AddSingleton(writer));

        // Act
        var channels = await host.Service(TenantKey.None).GetChannelsAsync(Ct);
        var types = await host.Service(TenantKey.None).GetMessageTypesAsync(Ct);

        // Assert
        Assert.Equal((Errors.Unavailable.Code, Errors.Unavailable.Code), (channels.Error?.Type.Code, types.Error?.Type.Code));
    }

    [Fact]
    public async Task GetChannelsAsync_NoStateRow_GivesNoCurrentRowForEachChannel()
    {
        // Arrange
        await using var host = await StartAsync(static services => RegistryTestHost.AddRegistry(services));

        // Act
        var channels = await host.Service(TenantKey.None).GetChannelsAsync(Ct);

        // Assert
        Assert.All(channels.Value, static channel => Assert.Null(channel.Current));
    }

    [Theory]
    [InlineData(ChannelDownReason.Errors)]
    [InlineData(ChannelDownReason.Operator)]
    public async Task GetChannelsAsync_DownChannelAfterAnUpRow_GivesItsDownRowWithTheDurationOfTheUpState(ChannelDownReason reason)
    {
        // Arrange
        var clock = TestHost.NewFakeTime();
        await using var host = await StartAsync(services => RegistryTestHost.AddRegistry(services).Services.AddSingleton<TimeProvider>(clock));
        var store = host.Server.GetRequiredService<IOutboxDispatchStore>();
        await store.SetChannelDownAsync(ADown(clock, ChannelDownReason.Operator), Ct);
        await store.SetChannelUpAsync(TestChannel.Id, reason: null, Now(clock), Ct);
        clock.Advance(TimeSpan.FromMinutes(5));
        var down = ADown(clock, reason);
        await store.SetChannelDownAsync(down, Ct);

        // Act
        var channels = await host.Service(TenantKey.None).GetChannelsAsync(Ct);

        // Assert
        var current = channels.Value[0].Current;
        Assert.Equal(
            (3, down, (ChannelStateKind?)ChannelStateKind.Up, (TimeSpan?)TimeSpan.FromMinutes(5)),
            (current?.Version, current?.ToDown(), current?.PreviousKind, current?.PreviousDuration));
        Assert.Null(channels.Value[1].Current);
    }

    [Fact]
    public async Task GetChannelsAsync_UpChannel_GivesItsLatestUpRowWithTheDurationOfTheDownState()
    {
        // Arrange
        var clock = TestHost.NewFakeTime();
        await using var host = await StartAsync(services => RegistryTestHost.AddRegistry(services).Services.AddSingleton<TimeProvider>(clock));
        var store = host.Server.GetRequiredService<IOutboxDispatchStore>();
        await store.SetChannelDownAsync(ADown(clock, ChannelDownReason.Operator), Ct);
        await store.SetChannelUpAsync(TestChannel.Id, reason: null, Now(clock).AddMinutes(1), Ct);
        await store.SetChannelDownAsync(ADown(clock, ChannelDownReason.Errors) with { SinceUtc = Now(clock).AddMinutes(1) }, Ct);
        await store.SetChannelUpAsync(TestChannel.Id, reason: null, Now(clock).AddMinutes(5), Ct);
        clock.Advance(TimeSpan.FromDays(1));

        // Act
        var channels = await host.Service(TenantKey.None).GetChannelsAsync(Ct);

        // Assert
        var current = channels.Value[0].Current;
        Assert.Equal(
            (ChannelStateKind.Up, Now(clock).AddDays(-1).AddMinutes(5), (ChannelStateKind?)ChannelStateKind.Down, (TimeSpan?)TimeSpan.FromMinutes(4)),
            (current!.Kind, current.SinceUtc, current.PreviousKind, current.PreviousDuration));
    }

    [Fact]
    public async Task GetChannelsAsync_UpRowBeforeTheWindow_GivesItAsTheCurrentRow()
    {
        // Arrange
        var clock = TestHost.NewFakeTime();
        await using var host = await StartAsync(services => RegistryTestHost.AddRegistry(services).Services.AddSingleton<TimeProvider>(clock));
        var store = host.Server.GetRequiredService<IOutboxDispatchStore>();
        var upAtUtc = Now(clock);
        await store.SetChannelDownAsync(ADown(clock, ChannelDownReason.Operator), Ct);
        await store.SetChannelUpAsync(TestChannel.Id, reason: null, upAtUtc, Ct);
        clock.Advance(ChannelState.HistoryWindow + TimeSpan.FromDays(10));

        // Act
        var channels = await host.Service(TenantKey.None).GetChannelsAsync(Ct);

        // Assert
        Assert.Equal((2, ChannelStateKind.Up, upAtUtc), (channels.Value[0].Current?.Version, channels.Value[0].Current?.Kind, channels.Value[0].Current?.SinceUtc));
    }

    [Fact]
    public async Task GetChannelHistoryAsync_RowsOfTwoChannels_GivesTheRowsOfTheChannelInTheWindowAndTheRowCurrentAtItsStartNewestFirst()
    {
        // Arrange
        var clock = TestHost.NewFakeTime();
        await using var host = await StartAsync(services => RegistryTestHost.AddRegistry(services).Services.AddSingleton<TimeProvider>(clock));
        var store = host.Server.GetRequiredService<IOutboxDispatchStore>();
        var startUtc = Now(clock);
        await store.SetChannelDownAsync(ADown(clock, ChannelDownReason.Operator), Ct);
        await store.SetChannelUpAsync(TestChannel.Id, reason: null, startUtc, Ct);
        clock.Advance(TimeSpan.FromDays(20));
        await store.SetChannelDownAsync(ADown(clock, ChannelDownReason.Errors), Ct);
        await store.SetChannelDownAsync(ADown(clock, ChannelDownReason.Operator, TestChannel.SecondId), Ct);
        clock.Advance(TimeSpan.FromDays(11));
        await store.SetChannelUpAsync(TestChannel.Id, reason: null, Now(clock), Ct);

        // Act
        var history = await host.Service(TenantKey.None).GetChannelHistoryAsync(TestChannel.Id, Ct);

        // Assert
        Assert.Equal(
            [
                (4, ChannelStateKind.Up, Now(clock), (ChannelStateKind?)ChannelStateKind.Down, (TimeSpan?)TimeSpan.FromDays(11)),
                (3, ChannelStateKind.Down, startUtc.AddDays(20), ChannelStateKind.Up, TimeSpan.FromDays(20)),
                (2, ChannelStateKind.Up, startUtc, ChannelStateKind.Down, TimeSpan.Zero),
            ],
            history.Value.Select(static row => (row.Version, row.Kind, row.SinceUtc, row.PreviousKind, row.PreviousDuration)));
    }

    [Fact]
    public async Task GetChannelHistoryAsync_ChannelThatIsNotRegistered_GivesNotFound()
    {
        // Arrange
        await using var host = await StartAsync(static services => RegistryTestHost.AddRegistry(services));

        // Act
        var history = await host.Service(TenantKey.None).GetChannelHistoryAsync(new ChannelId("Unknown"), Ct);

        // Assert
        Assert.Equal(Errors.NotFound.Code, history.Error?.Type.Code);
    }

    [Fact]
    public async Task Queries_StoreError_GiveTheErrorOfTheOutboxDispatchStore()
    {
        // Arrange
        var store = Substitute.For<IOutboxDispatchStore>();
        store.GetChannelStatesAsync(default, Ct).ReturnsForAnyArgs(
            ValueTask.FromResult(ErrorOr<IReadOnlyList<ChannelState>>.ForError(new Error(Errors.Unavailable, "The outbox store is not available."))));
        await using var host = await StartAsync(services => RegistryTestHost.AddRegistry(services).Services.AddSingleton(store));

        // Act
        var channels = await host.Service(TenantKey.None).GetChannelsAsync(Ct);
        var history = await host.Service(TenantKey.None).GetChannelHistoryAsync(TestChannel.Id, Ct);

        // Assert
        Assert.Equal((Errors.Unavailable.Code, Errors.Unavailable.Code), (channels.Error?.Type.Code, history.Error?.Type.Code));
    }

    /// <summary>Starts a host of the implementation, whose server has the services of a test.</summary>
    /// <param name="addServerServices">Adds the registry of the test, after the configuration, the clock, and the logging of the host.</param>
    /// <returns>The started host.</returns>
    protected abstract ValueTask<IRegistryQueriesTestHost<T>> StartAsync(Action<IServiceCollection> addServerServices);

    private static DateTime Now(TimeProvider clock) => clock.GetUtcNow().UtcDateTime;

    private static ChannelDown ADown(TimeProvider clock, ChannelDownReason reason, ChannelId? channel = null) =>
        reason == ChannelDownReason.Errors
            ? new ChannelDown(channel ?? TestChannel.Id, reason, Now(clock), Now(clock).AddSeconds(30), 503, "Unavailable", Note: null)
            : new ChannelDown(channel ?? TestChannel.Id, reason, Now(clock), NextCheckAtUtc: null, ErrorCode: null, ErrorName: null, "maintenance");

    private static string EntryTextOf(MessageTypeEntry entry) =>
        $"{entry.Type.Name} name={entry.DisplayName} description={entry.Description} enabled={entry.Enabled} "
        + $"disabled={string.Join(',', entry.DisabledChannels.Select(static channel => channel.Name))} revision={entry.Revision} "
        + $"modified={entry.LastModifiedAtUtc:O}";

    private static string TextOf(RegistryMessageType type) =>
        $"{type.Type.Name} channels={string.Join(',', type.Channels.Select(static channel => channel.Name))} name={type.DisplayName} "
        + $"description={type.Description} enabled={type.Enabled} disabled={string.Join(',', type.DisabledChannels.Select(static channel => channel.Name))} "
        + $"revision={type.Revision}";
}
