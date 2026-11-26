using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Routing;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using static MsgHatch.Domain.Tests.Unit.TestTypes.RegistryTestHost;

namespace MsgHatch.Domain.Tests.Unit.Routing;

/// <summary>
/// The stored registry of <c>UseStoredRegistry</c> (section 4.20): the merge of the code with the entries, and the
/// refresh on the timer of the fake clock. Each store substitute answers at once, except where a test needs a read
/// that has not ended.
/// </summary>
public sealed partial class StoredRegistryUnitTests
{
    private static readonly TimeSpan _interval = TimeSpan.Parse(RefreshInterval, System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public async Task FindChannel_EntryWithAllValues_GivesTheNameTheDescriptionAndTheEnabledFlagOfTheEntry()
    {
        // Arrange
        const string displayName = "Shop channel";
        const string description = "Stored description";
        var entry = AChannelEntry(displayName: displayName, description: description, enabled: false, revision: 1);
        var store = AStoreThatReads(new RegistryEntries([entry], []));
        await using var provider = Build(store);

        // Act
        var channel = provider.GetRequiredService<IMessageRegistry>().FindChannel(TestChannel.Id);

        // Assert
        Assert.Equal<ChannelDescriptor?>(
            TestChannel.Descriptor with { Name = displayName, Description = description, Enabled = false },
            channel);
        _ = store.Received(1).GetEntriesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FindChannel_EntryWithNoTexts_KeepsTheNameAndTheDescriptionOfTheCode()
    {
        // Arrange
        var entry = AChannelEntry(DescribedChannel.Id, displayName: null, description: null, enabled: false, revision: 1);
        var store = AStoreThatReads(new RegistryEntries([entry], []));
        await using var provider = Build(store);

        // Act
        var channel = provider.GetRequiredService<IMessageRegistry>().FindChannel(DescribedChannel.Id);

        // Assert
        Assert.Equal<ChannelDescriptor?>(DescribedChannel with { Enabled = false }, channel);
        _ = store.Received(1).GetEntriesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Channels_OneChannelWithAnEntry_KeepsTheValuesOfTheCodeForTheOtherChannels()
    {
        // Arrange
        const string displayName = "Group channel";
        var entry = AChannelEntry(TestChannel.SecondId, displayName: displayName, revision: 1);
        var store = AStoreThatReads(new RegistryEntries([entry], []));
        await using var provider = Build(store);

        // Act
        var channels = provider.GetRequiredService<IMessageRegistry>().Channels;

        // Assert
        Assert.Equal(
            [TestChannel.Descriptor, TestChannel.SecondDescriptor with { Name = displayName, Description = entry.Description }, TestChannel.ThirdDescriptor, DescribedChannel],
            channels);
        _ = store.Received(1).GetEntriesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FindMessageType_EntryWithAllValues_GivesTheValuesOfTheEntryAndKeepsTheChannelsOfTheCode()
    {
        // Arrange
        const string displayName = "Shop event";
        const string description = "Stored description";
        var entry = AMessageTypeEntry(
            disabledChannels: [TestChannel.ThirdId], displayName: displayName, description: description, enabled: false,
            revision: 1);
        var store = AStoreThatReads(new RegistryEntries([], [entry]));
        await using var provider = Build(store);

        // Act
        var type = provider.GetRequiredService<IMessageRegistry>().FindMessageType(TestEventType);

        // Assert
        Assert.Equal(
            ("TestEvent", "Test,Third", displayName, description, false, "Third"),
            (type!.Type.Name, string.Join(',', type.Channels.Select(static c => c.Name)), type.DisplayName, type.Description,
                type.Enabled, string.Join(',', type.DisabledChannels!.Select(static c => c.Name))));
        _ = store.Received(1).GetEntriesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MessageTypes_OneMessageTypeWithAnEntry_KeepsTheValuesOfTheCodeForTheOtherMessageType()
    {
        // Arrange
        var entry = AMessageTypeEntry(enabled: false, revision: 1);
        var store = AStoreThatReads(new RegistryEntries([], [entry]));
        await using var provider = Build(store);

        // Act
        var types = provider.GetRequiredService<IMessageRegistry>().MessageTypes;

        // Assert
        Assert.Equal(
            [("TestEvent", false, entry.DisplayName), ("OtherTestEvent", true, null)],
            types.Select(static type => (type.Type.Name, type.Enabled, type.DisplayName)));
        Assert.Null(types[1].DisabledChannels);
        _ = store.Received(1).GetEntriesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FindChannel_EntryOfAChannelThatTheCodeDoesNotRegister_GivesNull()
    {
        // Arrange
        var store = AStoreThatReads(new RegistryEntries([AChannelEntry(new ChannelId("Removed"))], []));
        await using var provider = Build(store);

        // Act
        var channel = provider.GetRequiredService<IMessageRegistry>().FindChannel(new ChannelId("Removed"));

        // Assert
        Assert.Null(channel);
        _ = store.Received(1).GetEntriesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FindChannel_FirstReadThatHasNotEnded_GivesTheValuesOfTheCode()
    {
        // Arrange
        var read = new TaskCompletionSource<ErrorOr<RegistryEntries>>();
        var store = Substitute.For<IRegistrySettingsStore>();
        store.GetEntriesAsync(Ct).ReturnsForAnyArgs(new ValueTask<ErrorOr<RegistryEntries>>(read.Task));
        await using var provider = Build(store);

        // Act
        var channel = provider.GetRequiredService<IMessageRegistry>().FindChannel(TestChannel.Id);

        // Assert
        Assert.Same(TestChannel.Descriptor, channel);
        _ = store.Received(1).GetEntriesAsync(Arg.Any<CancellationToken>());
    }

    public static TheoryData<string, int> RefreshRows => new()
    {
        { "00:00:09.999", 1 },
        { "00:00:10", 2 },
        { "00:00:10.001", 2 },
    };

    [Theory]
    [MemberData(nameof(RefreshRows))]
    public async Task RefreshInterval_TimeAfterTheFirstRead_ReadsTheStoreAgainOnlyAtTheInterval(string elapsed, int expectedReads)
    {
        // Arrange
        var time = TestHost.NewFakeTime();
        var store = AStoreThatReads(NoEntries());
        await using var provider = Build(store, time);
        _ = provider.GetRequiredService<IMessageRegistry>();

        // Act
        time.Advance(TimeSpan.Parse(elapsed, System.Globalization.CultureInfo.InvariantCulture));

        // Assert
        await store.Received(expectedReads).GetEntriesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefreshInterval_WriteOfAnotherProcess_IsVisibleOnlyAfterTheInterval()
    {
        // Arrange
        var time = TestHost.NewFakeTime();
        await using var provider = Build(time: time);
        var registry = provider.GetRequiredService<IMessageRegistry>();
        var entry = AChannelEntry(revision: 1);
        await provider.GetRequiredService<InMemoryRegistrySettingsStore>().SaveChannelEntryAsync(entry, Ct);
        var before = registry.FindChannel(TestChannel.Id)!.Name;

        // Act
        time.Advance(_interval);

        // Assert
        Assert.Equal(("Test channel", entry.DisplayName), (before, registry.FindChannel(TestChannel.Id)!.Name));
    }

    [Fact]
    public async Task Refresh_StoreErrorAfterASuccessfulRead_KeepsTheLastSnapshotAndLogsAWarning()
    {
        // Arrange
        var time = TestHost.NewFakeTime();
        var entry = AChannelEntry(revision: 1);
        var store = Substitute.For<IRegistrySettingsStore>();
        store.GetEntriesAsync(Ct).ReturnsForAnyArgs(
            ValueTask.FromResult(ErrorOr<RegistryEntries>.ForValue(new RegistryEntries([entry], []))),
            ValueTask.FromResult(ErrorOr<RegistryEntries>.ForError(new Error(Errors.Unavailable))));
        await using var provider = Build(store, time, enableLogging: false);
        var registry = provider.GetRequiredService<IMessageRegistry>();

        // Act
        time.Advance(_interval);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal((entry.DisplayName, LogLevel.Warning, 2908), (registry.FindChannel(TestChannel.Id)!.Name, record.Level, record.Id.Id));
        _ = store.Received(2).GetEntriesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refresh_StoreThatThrows_KeepsTheValuesOfTheCodeAndLogsAWarning()
    {
        // Arrange
        var store = Substitute.For<IRegistrySettingsStore>();
        store.GetEntriesAsync(Ct).ThrowsForAnyArgs(new InvalidOperationException("A bug of a consumer store."));
        await using var provider = Build(store, enableLogging: false);

        // Act
        var channel = provider.GetRequiredService<IMessageRegistry>().FindChannel(TestChannel.Id);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal(((ChannelDescriptor?)TestChannel.Descriptor, LogLevel.Warning, 2908), (channel, record.Level, record.Id.Id));
        _ = store.Received(1).GetEntriesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refresh_ReadThatHasNotEndedAtTheNextInterval_StartsNoSecondRead()
    {
        // Arrange
        var time = TestHost.NewFakeTime();
        var read = new TaskCompletionSource<ErrorOr<RegistryEntries>>();
        var store = Substitute.For<IRegistrySettingsStore>();
        store.GetEntriesAsync(Ct).ReturnsForAnyArgs(new ValueTask<ErrorOr<RegistryEntries>>(read.Task));
        await using var provider = Build(store, time);
        _ = provider.GetRequiredService<IMessageRegistry>();

        // Act
        time.Advance(_interval * 2);

        // Assert
        await store.Received(1).GetEntriesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refresh_ReadThatAnOwnWriteOvertakes_DoesNotHideTheWrite()
    {
        // Arrange
        const string displayName = "Saved during the read";
        var time = TestHost.NewFakeTime();
        IRegistryWriter? writer = null;
        var store = AStoreThatReads(NoEntries());
        store.GetEntriesAsync(Ct).ReturnsForAnyArgs(
            _ => ValueTask.FromResult(ErrorOr<RegistryEntries>.ForValue(NoEntries())),
            async ValueTask<ErrorOr<RegistryEntries>> (call) =>
            {
                _ = await writer!.SaveChannelAsync(AChannelEntry(displayName: displayName), Ct);
                return NoEntries();
            });
        await using var provider = Build(store, time);
        var registry = provider.GetRequiredService<IMessageRegistry>();
        writer = provider.GetRequiredService<IRegistryWriter>();

        // Act
        time.Advance(_interval);

        // Assert
        Assert.Equal(displayName, registry.FindChannel(TestChannel.Id)!.Name);
        _ = store.Received(2).GetEntriesAsync(Arg.Any<CancellationToken>());
        _ = store.Received(1).SaveChannelEntryAsync(
            Arg.Is<ChannelEntry>(entry => entry.Channel == TestChannel.Id && entry.DisplayName == displayName && entry.Revision == 1), Ct);
    }

    [Fact]
    public async Task UseStoredRegistry_EnableLoggingFalse_TheReaderAndTheWriterAreOneRegistry()
    {
        // Arrange
        await using var provider = Build(enableLogging: false);

        // Act
        var writer = provider.GetRequiredService<IRegistryWriter>();

        // Assert
        Assert.Same(provider.GetRequiredService<IMessageRegistry>(), writer);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;
}
