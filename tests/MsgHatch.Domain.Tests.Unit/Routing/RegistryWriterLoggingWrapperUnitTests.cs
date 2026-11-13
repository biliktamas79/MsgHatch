using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Routing;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using static MsgHatch.Domain.Tests.Unit.TestTypes.RegistryTestHost;

namespace MsgHatch.Domain.Tests.Unit.Routing;

/// <summary>
/// The standard logging wrapper tests for <see cref="IRegistryWriter"/>. The settings store has a logging wrapper of
/// its own, so each test reads only the records of the writer, with the event ids 2904 to 2907.
/// </summary>
public sealed class RegistryWriterLoggingWrapperUnitTests
{
    private const string _displayName = "Shop name of client1@example.com";

    private const string _description = "Secret description";

    public static TheoryData<string, int> FailedEventRows => new()
    {
        { nameof(IRegistryWriter.GetEntriesAsync), 2905 },
        { nameof(IRegistryWriter.SaveChannelAsync), 2907 },
        { nameof(IRegistryWriter.SaveMessageTypeAsync), 2907 },
        { nameof(IRegistryWriter.ResetChannelAsync), 2907 },
        { nameof(IRegistryWriter.ResetMessageTypeAsync), 2907 },
    };

    public static TheoryData<string, LogLevel, int> SucceededEventRows => new()
    {
        { nameof(IRegistryWriter.GetEntriesAsync), LogLevel.Debug, 2904 },
        { nameof(IRegistryWriter.SaveChannelAsync), LogLevel.Information, 2906 },
        { nameof(IRegistryWriter.SaveMessageTypeAsync), LogLevel.Information, 2906 },
        { nameof(IRegistryWriter.ResetChannelAsync), LogLevel.Information, 2906 },
        { nameof(IRegistryWriter.ResetMessageTypeAsync), LogLevel.Information, 2906 },
    };

    [Theory]
    [MemberData(nameof(FailedEventRows))]
    public async Task Member_TransientError_ReturnsTheSameErrorAndLogsAWarningWithItsEventId(string member, int eventId)
    {
        // Arrange
        var expected = new Error(Errors.Unavailable);
        await using var provider = Build(AStoreThatFails(expected));

        // Act
        var error = await CallAsync(provider.GetRequiredService<IRegistryWriter>(), member);

        // Assert
        var record = Assert.Single(WriterRecords(provider));
        Assert.Equal((expected, LogLevel.Warning, eventId), (error, record.Level, record.Id.Id));
    }

    [Theory]
    [MemberData(nameof(FailedEventRows))]
    public async Task Member_PermanentError_LogsAnErrorWithItsEventId(string member, int eventId)
    {
        // Arrange
        await using var provider = Build(AStoreThatFails(new Error(Errors.Conflict)));

        // Act
        await CallAsync(provider.GetRequiredService<IRegistryWriter>(), member);

        // Assert
        var record = Assert.Single(WriterRecords(provider));
        Assert.Equal((LogLevel.Error, eventId), (record.Level, record.Id.Id));
    }

    [Theory]
    [MemberData(nameof(FailedEventRows))]
    public async Task Member_Cancelled_LogsAnInformationRecordWithItsEventId(string member, int eventId)
    {
        // Arrange
        await using var provider = Build(AStoreThatFails(new Error(Errors.OperationCancelled)));

        // Act
        await CallAsync(provider.GetRequiredService<IRegistryWriter>(), member);

        // Assert
        var record = Assert.Single(WriterRecords(provider));
        Assert.Equal((LogLevel.Information, eventId), (record.Level, record.Id.Id));
    }

    [Theory]
    [MemberData(nameof(SucceededEventRows))]
    public async Task Member_Success_LogsARecordWithItsLevelAndEventId(string member, LogLevel level, int eventId)
    {
        // Arrange
        await using var provider = Build();

        // Act
        var error = await CallAsync(provider.GetRequiredService<IRegistryWriter>(), member);

        // Assert
        var record = Assert.Single(WriterRecords(provider));
        Assert.Equal(((Error?)null, level, eventId), (error, record.Level, record.Id.Id));
    }

    [Fact]
    public async Task SaveChannelAsync_Success_LogsTheNameAndTheSavedRevisionAndNoText()
    {
        // Arrange
        await using var provider = Build();

        // Act
        await provider.GetRequiredService<IRegistryWriter>().SaveChannelAsync(ATextChannelEntry(), Ct);

        // Assert
        var record = Assert.Single(WriterRecords(provider));
        Assert.Equal(("Test", "1"), (record.GetStructuredStateValue("Entry"), record.GetStructuredStateValue("Revision")));
        Assert.False(record.ContainsText(_displayName) || record.ContainsText(_description));
    }

    [Fact]
    public async Task SaveMessageTypeAsync_Conflict_LogsTheExpectedRevisionAndNoText()
    {
        // Arrange
        await using var provider = Build(AStoreThatFails(new Error(Errors.Conflict)));

        // Act
        await provider.GetRequiredService<IRegistryWriter>().SaveMessageTypeAsync(ATextMessageTypeEntry() with { Revision = 4 }, Ct);

        // Assert
        var record = Assert.Single(WriterRecords(provider));
        Assert.Equal(("TestEvent", "4"), (record.GetStructuredStateValue("Entry"), record.GetStructuredStateValue("Revision")));
        Assert.False(record.ContainsText(_displayName) || record.ContainsText(_description));
    }

    [Fact]
    public async Task UseStoredRegistry_EnableLoggingTrue_WrapsTheWriterAndNotTheReader()
    {
        // Arrange
        await using var provider = Build();

        // Act
        var writer = provider.GetRequiredService<IRegistryWriter>();

        // Assert
        Assert.NotSame(provider.GetRequiredService<IMessageRegistry>(), writer);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ChannelEntry ATextChannelEntry() => AChannelEntry(displayName: _displayName, description: _description);

    private static MessageTypeEntry ATextMessageTypeEntry() => AMessageTypeEntry(displayName: _displayName, description: _description);

    private static FakeLogRecord[] WriterRecords(IServiceProvider provider) =>
        [.. provider.LogRecords().Where(static record => record.Id.Id is >= 2904 and <= 2907)];

    private static async Task<Error?> CallAsync(IRegistryWriter writer, string member) => member switch
    {
        nameof(IRegistryWriter.GetEntriesAsync) => (await writer.GetEntriesAsync(Ct)).Error,
        nameof(IRegistryWriter.SaveChannelAsync) => (await writer.SaveChannelAsync(ATextChannelEntry(), Ct)).Error,
        nameof(IRegistryWriter.SaveMessageTypeAsync) => (await writer.SaveMessageTypeAsync(ATextMessageTypeEntry(), Ct)).Error,
        nameof(IRegistryWriter.ResetChannelAsync) => await writer.ResetChannelAsync(TestChannel.Id, Ct),
        _ => await writer.ResetMessageTypeAsync(TestEventType, Ct),
    };
}
