using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Routing;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using NSubstitute;
using static MsgHatch.Domain.Tests.Unit.TestTypes.RegistryTestHost;

namespace MsgHatch.Domain.Tests.Unit.Routing;

/// <summary>The writer of the stored registry: the save, the reset, and the validation of the entries (section 4.20, D171).</summary>
public sealed partial class StoredRegistryUnitTests
{
    public static TheoryData<string, string?, string?, int, bool> ChannelEntryRows => new()
    {
        { "no texts", null, null, 0, true },
        { "display name empty", "", null, 0, false },
        { "display name white space", " \t", null, 0, false },
        { "display name accented and special", "Értesítő ő ű \" \\ < &", null, 0, true },
        { "display name 199 characters", new string('n', 199), null, 0, true },
        { "display name 200 characters", new string('n', 200), null, 0, true },
        { "display name 201 characters", new string('n', 201), null, 0, false },
        { "description empty", null, "", 0, false },
        { "description white space", null, "  ", 0, false },
        { "description 1999 characters", null, new string('d', 1999), 0, true },
        { "description 2000 characters", null, new string('d', 2000), 0, true },
        { "description 2001 characters", null, new string('d', 2001), 0, false },
        { "revision -1", null, null, -1, false },
        { "revision 1", null, null, 1, true },
    };

    public static TheoryData<string, ChannelId[]?, string?, int, bool> MessageTypeEntryRows => new()
    {
        { "disabled channels null", null, null, 0, false },
        { "disabled channels empty", [], null, 0, true },
        { "one disabled channel of the message type", [new ChannelId("Third")], null, 0, true },
        { "two disabled channels of the message type", [new ChannelId("Third"), new ChannelId("Test")], null, 0, true },
        { "disabled channel of another message type", [new ChannelId("Second")], null, 0, false },
        { "unknown disabled channel", [new ChannelId("Unknown")], null, 0, false },
        { "duplicate disabled channel", [new ChannelId("Third"), new ChannelId("Third")], null, 0, false },
        { "display name empty", [], "", 0, false },
        { "display name 201 characters", [], new string('n', 201), 0, false },
        { "revision -1", [], null, -1, false },
    };

    [Theory]
    [MemberData(nameof(ChannelEntryRows))]
    public async Task SaveChannelAsync_ValidationRule_GivesValidationFailedAndCallsNoStoreOnlyWhenInvalid(
        string row, string? displayName, string? description, int revision, bool valid)
    {
        // Arrange
        var store = AStoreThatReads(NoEntries());
        await using var provider = Build(store);
        var entry = AChannelEntry(displayName: displayName, description: description, revision: revision);

        // Act
        var saved = await provider.GetRequiredService<IRegistryWriter>().SaveChannelAsync(entry, Ct);

        // Assert
        Assert.Equal((row, !valid, valid ? 1 : 0), (row, saved.Error?.Type == Errors.ValidationFailed, SaveCalls(store)));
    }

    [Theory]
    [MemberData(nameof(MessageTypeEntryRows))]
    public async Task SaveMessageTypeAsync_ValidationRule_GivesValidationFailedAndCallsNoStoreOnlyWhenInvalid(
        string row, ChannelId[]? disabledChannels, string? displayName, int revision, bool valid)
    {
        // Arrange
        var store = AStoreThatReads(NoEntries());
        await using var provider = Build(store);
        var entry = AMessageTypeEntry(displayName: displayName, revision: revision) with { DisabledChannels = disabledChannels! };

        // Act
        var saved = await provider.GetRequiredService<IRegistryWriter>().SaveMessageTypeAsync(entry, Ct);

        // Assert
        Assert.Equal((row, !valid, valid ? 1 : 0), (row, saved.Error?.Type == Errors.ValidationFailed, SaveCalls(store)));
    }

    [Fact]
    public async Task SaveChannelAsync_UnregisteredChannel_GivesNotFoundAndCallsNoStore()
    {
        // Arrange
        var store = AStoreThatReads(NoEntries());
        await using var provider = Build(store);

        // Act
        var saved = await provider.GetRequiredService<IRegistryWriter>().SaveChannelAsync(AChannelEntry(new ChannelId("Unknown")), Ct);

        // Assert
        Assert.Equal((Errors.NotFound, 0), (saved.Error?.Type, SaveCalls(store)));
    }

    [Fact]
    public async Task SaveMessageTypeAsync_UnregisteredMessageType_GivesNotFoundAndCallsNoStore()
    {
        // Arrange
        var store = AStoreThatReads(NoEntries());
        await using var provider = Build(store);

        // Act
        var saved = await provider.GetRequiredService<IRegistryWriter>().SaveMessageTypeAsync(AMessageTypeEntry(new MessageType("Unknown")), Ct);

        // Assert
        Assert.Equal((Errors.NotFound, 0), (saved.Error?.Type, SaveCalls(store)));
    }

    [Fact]
    public async Task SaveChannelAsync_NewEntry_SavesTheRevision1WithTheTimeOfTheSave()
    {
        // Arrange
        var time = TestHost.NewFakeTime();
        await using var provider = Build(time: time);
        var writer = provider.GetRequiredService<IRegistryWriter>();
        time.Advance(TimeSpan.FromMinutes(5));
        var expected = AChannelEntry(revision: 1) with { LastModifiedAtUtc = TestHost.Start.UtcDateTime.AddMinutes(5) };

        // Act
        var saved = await writer.SaveChannelAsync(AChannelEntry(revision: 0), Ct);

        // Assert
        Assert.Equal(expected, saved.Value);
        Assert.Equal(expected, Assert.Single((await writer.GetEntriesAsync(Ct)).Value.Channels));
    }

    [Fact]
    public async Task SaveMessageTypeAsync_StoredEntry_SavesTheNextRevision()
    {
        // Arrange
        await using var provider = Build();
        var writer = provider.GetRequiredService<IRegistryWriter>();
        await writer.SaveMessageTypeAsync(AMessageTypeEntry(revision: 0), Ct);

        // Act
        var saved = await writer.SaveMessageTypeAsync(AMessageTypeEntry(disabledChannels: [TestChannel.ThirdId], revision: 1), Ct);

        // Assert
        var stored = Assert.Single((await writer.GetEntriesAsync(Ct)).Value.MessageTypes);
        Assert.Equal((2, "Third"), (saved.Value.Revision, string.Join(',', stored.DisabledChannels.Select(static c => c.Name))));
        Assert.Equal(saved.Value with { DisabledChannels = [] }, stored with { DisabledChannels = [] });
    }

    [Fact]
    public async Task SaveChannelAsync_SavedEntry_TheReaderGivesItAtOnce()
    {
        // Arrange
        await using var provider = Build();
        var registry = provider.GetRequiredService<IMessageRegistry>();

        // Act
        await provider.GetRequiredService<IRegistryWriter>().SaveChannelAsync(AChannelEntry(displayName: "Saved now", enabled: false), Ct);

        // Assert
        Assert.Equal(("Saved now", false), (registry.FindChannel(TestChannel.Id)!.Name, registry.FindChannel(TestChannel.Id)!.Enabled));
    }

    [Fact]
    public async Task SaveMessageTypeAsync_SavedEntry_TheReaderGivesItAtOnce()
    {
        // Arrange
        await using var provider = Build();
        var registry = provider.GetRequiredService<IMessageRegistry>();

        // Act
        await provider.GetRequiredService<IRegistryWriter>().SaveMessageTypeAsync(AMessageTypeEntry(disabledChannels: [TestChannel.ThirdId]), Ct);

        // Assert
        Assert.Equal([TestChannel.ThirdId], registry.FindMessageType(TestEventType)!.DisabledChannels!);
    }

    [Fact]
    public async Task SaveChannelAsync_ExpectedRevisionThatIsNotStored_GivesConflictAndKeepsTheStoredValues()
    {
        // Arrange
        await using var provider = Build();
        var registry = provider.GetRequiredService<IMessageRegistry>();
        var writer = provider.GetRequiredService<IRegistryWriter>();
        await writer.SaveChannelAsync(AChannelEntry(displayName: "First", revision: 0), Ct);

        // Act
        var saved = await writer.SaveChannelAsync(AChannelEntry(displayName: "Second", revision: 0), Ct);

        // Assert
        Assert.Equal((Errors.Conflict, "First"), (saved.Error?.Type, registry.FindChannel(TestChannel.Id)!.Name));
    }

    [Fact]
    public async Task SaveChannelAsync_StoreError_GivesTheErrorAndKeepsTheValuesOfTheCode()
    {
        // Arrange
        var expected = new Error(Errors.Unavailable);
        await using var provider = Build(AStoreThatFails(expected), enableLogging: false);
        var registry = provider.GetRequiredService<IMessageRegistry>();

        // Act
        var saved = await provider.GetRequiredService<IRegistryWriter>().SaveChannelAsync(AChannelEntry(), Ct);

        // Assert
        Assert.Equal((expected, "Test channel"), (saved.Error, registry.FindChannel(TestChannel.Id)!.Name));
    }

    [Fact]
    public async Task SaveMessageTypeAsync_StoreError_GivesTheErrorAndKeepsTheValuesOfTheCode()
    {
        // Arrange
        var expected = new Error(Errors.Unavailable);
        await using var provider = Build(AStoreThatFails(expected), enableLogging: false);
        var registry = provider.GetRequiredService<IMessageRegistry>();

        // Act
        var saved = await provider.GetRequiredService<IRegistryWriter>().SaveMessageTypeAsync(AMessageTypeEntry(enabled: false), Ct);

        // Assert
        Assert.Equal((expected, true), (saved.Error, registry.FindMessageType(TestEventType)!.Enabled));
    }

    [Fact]
    public async Task ResetChannelAsync_SavedEntry_TheValuesOfTheCodeApplyAtOnce()
    {
        // Arrange
        await using var provider = Build();
        var registry = provider.GetRequiredService<IMessageRegistry>();
        var writer = provider.GetRequiredService<IRegistryWriter>();
        await writer.SaveChannelAsync(AChannelEntry(enabled: false), Ct);

        // Act
        var error = await writer.ResetChannelAsync(TestChannel.Id, Ct);

        // Assert
        Assert.Equal<(Error?, ChannelDescriptor?)>((null, TestChannel.Descriptor), (error, registry.FindChannel(TestChannel.Id)));
        Assert.Empty((await writer.GetEntriesAsync(Ct)).Value.Channels);
    }

    [Fact]
    public async Task ResetMessageTypeAsync_SavedEntry_TheValuesOfTheCodeApplyAtOnce()
    {
        // Arrange
        await using var provider = Build();
        var registry = provider.GetRequiredService<IMessageRegistry>();
        var writer = provider.GetRequiredService<IRegistryWriter>();
        await writer.SaveMessageTypeAsync(AMessageTypeEntry(enabled: false), Ct);

        // Act
        var error = await writer.ResetMessageTypeAsync(TestEventType, Ct);

        // Assert
        Assert.Equal(((Error?)null, true, (string?)null), (error, registry.FindMessageType(TestEventType)!.Enabled, registry.FindMessageType(TestEventType)!.DisplayName));
        Assert.Empty((await writer.GetEntriesAsync(Ct)).Value.MessageTypes);
    }

    [Fact]
    public async Task ResetChannelAsync_StoreError_GivesTheErrorAndKeepsTheStoredValues()
    {
        // Arrange
        var expected = new Error(Errors.Unavailable);
        var store = AStoreThatReads(new RegistryEntries([AChannelEntry(displayName: "Stored", revision: 1)], []));
        store.DeleteChannelEntryAsync(default, Ct).ReturnsForAnyArgs(ValueTask.FromResult<Error?>(expected));
        await using var provider = Build(store);
        var registry = provider.GetRequiredService<IMessageRegistry>();

        // Act
        var error = await provider.GetRequiredService<IRegistryWriter>().ResetChannelAsync(TestChannel.Id, Ct);

        // Assert
        Assert.Equal((expected, "Stored"), (error, registry.FindChannel(TestChannel.Id)!.Name));
    }

    [Fact]
    public async Task ResetMessageTypeAsync_StoreError_GivesTheErrorAndKeepsTheStoredValues()
    {
        // Arrange
        var expected = new Error(Errors.Unavailable);
        var store = AStoreThatReads(new RegistryEntries([], [AMessageTypeEntry(enabled: false, revision: 1)]));
        store.DeleteMessageTypeEntryAsync(default, Ct).ReturnsForAnyArgs(ValueTask.FromResult<Error?>(expected));
        await using var provider = Build(store);
        var registry = provider.GetRequiredService<IMessageRegistry>();

        // Act
        var error = await provider.GetRequiredService<IRegistryWriter>().ResetMessageTypeAsync(TestEventType, Ct);

        // Assert
        Assert.Equal((expected, false), (error, registry.FindMessageType(TestEventType)!.Enabled));
    }

    [Fact]
    public async Task GetEntriesAsync_StoreEntries_GivesTheEntriesOfTheStore()
    {
        // Arrange
        var entries = new RegistryEntries([AChannelEntry(revision: 3)], [AMessageTypeEntry(revision: 2)]);
        var store = AStoreThatReads(NoEntries(), entries);
        await using var provider = Build(store);

        // Act
        var read = await provider.GetRequiredService<IRegistryWriter>().GetEntriesAsync(Ct);

        // Assert
        Assert.Same(entries, read.Value);
    }

    private static int SaveCalls(IRegistrySettingsStore store) =>
        store.ReceivedCalls().Count(static call => call.GetMethodInfo().Name.StartsWith("Save", StringComparison.Ordinal));
}
