using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Management;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using NSubstitute;

namespace MsgHatch.Domain.Tests.Unit.Management;

/// <summary>
/// The standard logging wrapper tests for <see cref="IRegistryCommands"/> (D244, D283, D284). The wrapper wraps the registry
/// manager of <see cref="RegistryTestHost"/> with a transport of the channel Test, and each test reads only the records of
/// the events 2915 to 2918.
/// </summary>
public sealed class RegistryCommandsLoggingWrapperUnitTests
{
    private static readonly TenantKey _t1 = TenantKey.For(new TenantId("t1"));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task CheckChannelAsync_FailedCheck_LogsOneInformationRecordWithTheChannelTheErrorOfTheCheckAndTheChange()
    {
        // Arrange
        await using var provider = AProvider(new Error(Errors.Unavailable, "The provider is not available."));
        await using var scope = provider.CreateAsyncScope();

        // Act
        await scope.ServiceProvider.GetRequiredKeyedService<IRegistryCommands>(_t1).CheckChannelAsync(TestChannel.Id, Ct);

        // Assert
        var record = Assert.Single(RecordsOf(provider));
        Assert.Equal(
            (LogLevel.Information, 2915, "Test", "Unavailable", "True", "t1"),
            (record.Level, record.Id.Id, record.GetStructuredStateValue("Channel"), record.GetStructuredStateValue("CheckErrorName"),
                record.GetStructuredStateValue("Changed"), record.GetStructuredStateValue("Tenant")));
    }

    [Fact]
    public async Task CheckChannelAsync_Error_LogsOneRecordWithTheErrorTypeAndTheChannel()
    {
        // Arrange
        const string unknown = "Unknown";
        await using var provider = AProvider(checkError: null);
        await using var scope = provider.CreateAsyncScope();

        // Act
        await scope.ServiceProvider.GetRequiredKeyedService<IRegistryCommands>(TenantKey.None).CheckChannelAsync(new ChannelId(unknown), Ct);

        // Assert
        var record = Assert.Single(RecordsOf(provider));
        Assert.Equal(
            (2916, Errors.NotFound.Code.ToString(CultureInfo.InvariantCulture), unknown),
            (record.Id.Id, record.GetStructuredStateValue("ErrorCode"), record.GetStructuredStateValue("Channel")));
    }

    [Theory]
    [MemberData(nameof(RegistryTestHost.EntryCommandRows), MemberType = typeof(RegistryTestHost))]
    public async Task EntryCommand_Success_LogsOneInformationRecordWithTheEntryTheRevisionAndTheTenant(string command)
    {
        // Arrange
        await using var provider = AProvider(checkError: null, storedRegistry: true);
        await using var scope = provider.CreateAsyncScope();

        // Act
        var error = await RegistryTestHost.RunEntryCommandAsync(scope.ServiceProvider.GetRequiredKeyedService<IRegistryCommands>(_t1), command, Ct);

        // Assert
        var record = Assert.Single(RecordsOf(provider));
        Assert.Equal(
            ((Error?)null, LogLevel.Information, 2917, command, EntryOf(command), command.StartsWith("Save", StringComparison.Ordinal) ? "1" : null, "t1"),
            (error, record.Level, record.Id.Id, record.GetStructuredStateValue("Operation"), record.GetStructuredStateValue("Entry"),
                record.GetStructuredStateValue("Revision"), record.GetStructuredStateValue("Tenant")));
    }

    [Theory]
    [MemberData(nameof(RegistryTestHost.EntryCommandRows), MemberType = typeof(RegistryTestHost))]
    public async Task EntryCommand_Error_LogsOneRecordWithTheErrorTypeAndTheEntry(string command)
    {
        // Arrange
        await using var provider = AProvider(checkError: null);
        await using var scope = provider.CreateAsyncScope();

        // Act
        await RegistryTestHost.RunEntryCommandAsync(scope.ServiceProvider.GetRequiredKeyedService<IRegistryCommands>(TenantKey.None), command, Ct);

        // Assert
        var record = Assert.Single(RecordsOf(provider));
        Assert.Equal(
            (2918, Errors.NotFound.Code.ToString(CultureInfo.InvariantCulture), command, EntryOf(command)),
            (record.Id.Id, record.GetStructuredStateValue("ErrorCode"), record.GetStructuredStateValue("Operation"), record.GetStructuredStateValue("Entry")));
    }

    [Fact]
    public async Task Save_EntryWithTexts_LogsNoDisplayNameAndNoDescriptionInAnyRecord()
    {
        // Arrange
        await using var provider = AProvider(checkError: null, storedRegistry: true);
        await using var scope = provider.CreateAsyncScope();
        var commands = scope.ServiceProvider.GetRequiredKeyedService<IRegistryCommands>(_t1);

        // Act
        await commands.SaveChannelAsync(RegistryTestHost.AChannelEntry(displayName: "Secret name", description: "Secret description"), Ct);
        await commands.SaveMessageTypeAsync(RegistryTestHost.AMessageTypeEntry(displayName: "Secret name", description: "Secret description"), Ct);
        await commands.SaveChannelAsync(RegistryTestHost.AChannelEntry(displayName: "Secret name", description: "Secret description"), Ct);

        // Assert
        string?[] texts = [.. provider.LogRecords().SelectMany(static record => (record.StructuredState ?? []).Select(static state => state.Value).Append(record.Message))];
        Assert.Equal(
            (2, 1, 0),
            (RecordsOf(provider).Count(static record => record.Id.Id == 2917), RecordsOf(provider).Count(static record => record.Id.Id == 2918),
                texts.Count(static text => text?.Contains("Secret", StringComparison.Ordinal) == true)));
    }

    private static ServiceProvider AProvider(Error? checkError, bool storedRegistry = false)
    {
        var transport = Substitute.For<ITransport>();
        transport.Channel.Returns(TestChannel.Id);
        transport.CheckHealthAsync(Ct).ReturnsForAnyArgs(_ => ValueTask.FromResult(checkError));
        var services = TestHost.CreateServices();
        var builder = RegistryTestHost.AddRegistry(services).UseTransport(_ => transport);
        _ = storedRegistry ? builder.UseStoredRegistry() : builder;
        return services.Build();
    }

    private static string EntryOf(string command) =>
        command.EndsWith("ChannelAsync", StringComparison.Ordinal) ? TestChannel.Id.Name : RegistryTestHost.TestEventType.Name;

    private static IEnumerable<FakeLogRecord> RecordsOf(IServiceProvider provider) =>
        provider.LogRecords().Where(static record => record.Id.Id is >= 2915 and <= 2918);
}
