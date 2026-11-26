using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Routing;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using NSubstitute;

namespace MsgHatch.Domain.Tests.Unit.Routing;

/// <summary>The standard logging wrapper tests for <see cref="IRegistrySettingsStore"/>.</summary>
public sealed class RegistrySettingsStoreLoggingWrapperUnitTests
{
    public static TheoryData<string, int> FailedEventRows => new()
    {
        { nameof(IRegistrySettingsStore.GetEntriesAsync), 2901 },
        { nameof(IRegistrySettingsStore.SaveChannelEntryAsync), 2903 },
        { nameof(IRegistrySettingsStore.SaveMessageTypeEntryAsync), 2903 },
        { nameof(IRegistrySettingsStore.DeleteChannelEntryAsync), 2903 },
        { nameof(IRegistrySettingsStore.DeleteMessageTypeEntryAsync), 2903 },
    };

    public static TheoryData<string, int> SucceededEventRows => new()
    {
        { nameof(IRegistrySettingsStore.GetEntriesAsync), 2900 },
        { nameof(IRegistrySettingsStore.SaveChannelEntryAsync), 2902 },
        { nameof(IRegistrySettingsStore.SaveMessageTypeEntryAsync), 2902 },
        { nameof(IRegistrySettingsStore.DeleteChannelEntryAsync), 2902 },
        { nameof(IRegistrySettingsStore.DeleteMessageTypeEntryAsync), 2902 },
    };

    [Theory]
    [MemberData(nameof(FailedEventRows))]
    public async Task Member_TransientError_ReturnsTheSameErrorAndLogsAWarningWithItsEventId(string member, int eventId)
    {
        // Arrange
        var expected = new Error(Errors.Unavailable);
        var inner = AStoreOf(expected);
        await using var provider = BuildWith(inner);
        var store = provider.GetRequiredService<IRegistrySettingsStore>();

        // Act
        var error = await CallAsync(store, member);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal((expected, LogLevel.Warning, eventId), (error, record.Level, record.Id.Id));
        AssertOneInnerCallWithTheArgumentsOfCallAsync(inner, member);
    }

    [Theory]
    [MemberData(nameof(FailedEventRows))]
    public async Task Member_PermanentError_LogsAnErrorWithItsEventId(string member, int eventId)
    {
        // Arrange
        var inner = AStoreOf(new Error(Errors.Conflict));
        await using var provider = BuildWith(inner);
        var store = provider.GetRequiredService<IRegistrySettingsStore>();

        // Act
        await CallAsync(store, member);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal((LogLevel.Error, eventId), (record.Level, record.Id.Id));
        AssertOneInnerCallWithTheArgumentsOfCallAsync(inner, member);
    }

    [Theory]
    [MemberData(nameof(FailedEventRows))]
    public async Task Member_Cancelled_LogsAnInformationRecordWithItsEventId(string member, int eventId)
    {
        // Arrange
        var inner = AStoreOf(new Error(Errors.OperationCancelled));
        await using var provider = BuildWith(inner);
        var store = provider.GetRequiredService<IRegistrySettingsStore>();

        // Act
        await CallAsync(store, member);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal((LogLevel.Information, eventId), (record.Level, record.Id.Id));
        AssertOneInnerCallWithTheArgumentsOfCallAsync(inner, member);
    }

    [Theory]
    [MemberData(nameof(SucceededEventRows))]
    public async Task Member_Success_LogsADebugRecordWithItsEventId(string member, int eventId)
    {
        // Arrange
        var inner = AStoreOf(error: null);
        await using var provider = BuildWith(inner);
        var store = provider.GetRequiredService<IRegistrySettingsStore>();

        // Act
        var error = await CallAsync(store, member);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal(((Error?)null, LogLevel.Debug, eventId), (error, record.Level, record.Id.Id));
        AssertOneInnerCallWithTheArgumentsOfCallAsync(inner, member);
    }

    [Fact]
    public async Task GetEntriesAsync_InnerEntries_ReturnsTheEntriesUnchangedAndLogsTheirCounts()
    {
        // Arrange
        var entries = new RegistryEntries([AChannelEntry(), AChannelEntry() with { Channel = new ChannelId("SignalR") }], [AMessageTypeEntry()]);
        var inner = AStoreOf(error: null);
        inner.GetEntriesAsync(Ct).ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<RegistryEntries>.ForValue(entries)));
        await using var provider = BuildWith(inner);
        var store = provider.GetRequiredService<IRegistrySettingsStore>();

        // Act
        var result = await store.GetEntriesAsync(Ct);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Same(entries, result.Value);
        Assert.Equal(("2", "1"), (record.GetStructuredStateValue("ChannelCount"), record.GetStructuredStateValue("MessageTypeCount")));
        _ = inner.Received(1).GetEntriesAsync(Ct);
    }

    [Fact]
    public async Task SaveChannelEntryAsync_EntryWithTexts_LogsTheNameAndTheRevisionAndNoText()
    {
        // Arrange
        var inner = AStoreOf(error: null);
        await using var provider = BuildWith(inner);
        var store = provider.GetRequiredService<IRegistrySettingsStore>();
        var entry = AChannelEntry();

        // Act
        await store.SaveChannelEntryAsync(entry, Ct);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal(("Email", "3"), (record.GetStructuredStateValue("Entry"), record.GetStructuredStateValue("Revision")));
        Assert.False(record.ContainsText(_displayName) || record.ContainsText(_description));
        _ = inner.Received(1).SaveChannelEntryAsync(entry, Ct);
    }

    [Fact]
    public async Task SaveMessageTypeEntryAsync_EntryWithTexts_LogsNoText()
    {
        // Arrange
        var inner = AStoreOf(error: null);
        await using var provider = BuildWith(inner);
        var store = provider.GetRequiredService<IRegistrySettingsStore>();
        var entry = AMessageTypeEntry();

        // Act
        await store.SaveMessageTypeEntryAsync(entry, Ct);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.False(record.ContainsText(_displayName) || record.ContainsText(_description));
        _ = inner.Received(1).SaveMessageTypeEntryAsync(entry, Ct);
    }

    [Fact]
    public async Task UseRegistrySettingsStore_EnableLoggingFalse_RegistersNoLoggingWrapper()
    {
        // Arrange
        var inner = AStoreOf(error: null);
        await using var provider = BuildWith(inner, enableLogging: false);

        // Act
        var store = provider.GetRequiredService<IRegistrySettingsStore>();

        // Assert
        Assert.Same(inner, store);
    }

    private const string _displayName = "Shop email of client1@example.com";

    private const string _description = "Secret description";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ChannelEntry AChannelEntry() =>
        new(new ChannelId("Email"), _displayName, _description, Enabled: true, Revision: 3, TestHost.Start.UtcDateTime);

    private static MessageTypeEntry AMessageTypeEntry() =>
        new(new MessageType("A"), _displayName, _description, Enabled: true, [new ChannelId("Email")], Revision: 1, TestHost.Start.UtcDateTime);

    private static async Task<Error?> CallAsync(IRegistrySettingsStore store, string member) => member switch
    {
        nameof(IRegistrySettingsStore.GetEntriesAsync) => (await store.GetEntriesAsync(Ct)).Error,
        nameof(IRegistrySettingsStore.SaveChannelEntryAsync) => await store.SaveChannelEntryAsync(AChannelEntry(), Ct),
        nameof(IRegistrySettingsStore.SaveMessageTypeEntryAsync) => await store.SaveMessageTypeEntryAsync(AMessageTypeEntry(), Ct),
        nameof(IRegistrySettingsStore.DeleteChannelEntryAsync) => await store.DeleteChannelEntryAsync(new ChannelId("Email"), Ct),
        _ => await store.DeleteMessageTypeEntryAsync(new MessageType("A"), Ct),
    };

    private static void AssertOneInnerCallWithTheArgumentsOfCallAsync(IRegistrySettingsStore inner, string member)
    {
        switch (member)
        {
            case nameof(IRegistrySettingsStore.GetEntriesAsync):
                _ = inner.Received(1).GetEntriesAsync(Ct);
                break;
            case nameof(IRegistrySettingsStore.SaveChannelEntryAsync):
                _ = inner.Received(1).SaveChannelEntryAsync(AChannelEntry(), Ct);
                break;
            case nameof(IRegistrySettingsStore.SaveMessageTypeEntryAsync):
                _ = inner.Received(1).SaveMessageTypeEntryAsync(
                    Arg.Is<MessageTypeEntry>(entry => entry.Type == new MessageType("A") && entry.DisplayName == _displayName && entry.Revision == 1), Ct);
                break;
            case nameof(IRegistrySettingsStore.DeleteChannelEntryAsync):
                _ = inner.Received(1).DeleteChannelEntryAsync(new ChannelId("Email"), Ct);
                break;
            default:
                _ = inner.Received(1).DeleteMessageTypeEntryAsync(new MessageType("A"), Ct);
                break;
        }
    }

    private static IRegistrySettingsStore AStoreOf(Error? error)
    {
        var store = Substitute.For<IRegistrySettingsStore>();
        store.GetEntriesAsync(default).ReturnsForAnyArgs(ValueTask.FromResult(error is null
            ? ErrorOr<RegistryEntries>.ForValue(new RegistryEntries([], []))
            : ErrorOr<RegistryEntries>.ForError(error)));
        store.SaveChannelEntryAsync(default!, default).ReturnsForAnyArgs(ValueTask.FromResult(error));
        store.SaveMessageTypeEntryAsync(default!, default).ReturnsForAnyArgs(ValueTask.FromResult(error));
        store.DeleteChannelEntryAsync(default, default).ReturnsForAnyArgs(ValueTask.FromResult(error));
        store.DeleteMessageTypeEntryAsync(default, default).ReturnsForAnyArgs(ValueTask.FromResult(error));
        return store;
    }

    private static ServiceProvider BuildWith(IRegistrySettingsStore inner, bool enableLogging = true)
    {
        var services = TestHost.CreateServices();
        services.AddMsgHatch(o => o.EnableLogging = enableLogging).UseRegistrySettingsStore(_ => inner);
        return services.Build();
    }
}
