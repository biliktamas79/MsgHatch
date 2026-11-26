using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Management;
using MsgHatch.Domain.Routing;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using NSubstitute;

namespace MsgHatch.Domain.Tests.Unit.Management;

/// <summary>
/// The standard logging wrapper tests for <see cref="IRegistryQueries"/> (D244, D269). The wrapper wraps the registry
/// manager of <see cref="RegistryTestHost"/>, and each test reads only the records of the events 2909 to 2914.
/// </summary>
public sealed class RegistryQueriesLoggingWrapperUnitTests
{
    private static readonly TenantKey _t1 = TenantKey.For(new TenantId("t1"));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task GetChannelsAsync_Success_LogsOneDebugRecordWithTheCountAndTheTenantOfTheKey()
    {
        // Arrange
        await using var provider = AProvider();
        await using var scope = provider.CreateAsyncScope();

        // Act
        await scope.ServiceProvider.GetRequiredKeyedService<IRegistryQueries>(_t1).GetChannelsAsync(Ct);

        // Assert
        var record = Assert.Single(RecordsOf(provider));
        Assert.Equal(
            (LogLevel.Debug, 2909, "4", "t1"),
            (record.Level, record.Id.Id, record.GetStructuredStateValue("Count"), record.GetStructuredStateValue("Tenant")));
    }

    [Fact]
    public async Task GetMessageTypesAsync_Success_LogsOneDebugRecordWithTheCountAndTheTenantOfTheKey()
    {
        // Arrange
        await using var provider = AProvider();
        await using var scope = provider.CreateAsyncScope();

        // Act
        await scope.ServiceProvider.GetRequiredKeyedService<IRegistryQueries>(_t1).GetMessageTypesAsync(Ct);

        // Assert
        var record = Assert.Single(RecordsOf(provider));
        Assert.Equal(
            (LogLevel.Debug, 2911, "2", "t1"),
            (record.Level, record.Id.Id, record.GetStructuredStateValue("Count"), record.GetStructuredStateValue("Tenant")));
    }

    [Fact]
    public async Task Queries_WriterError_LogOneRecordForEachCallWithTheErrorType()
    {
        // Arrange
        var writer = Substitute.For<IRegistryWriter>();
        writer.GetEntriesAsync(Ct).ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<RegistryEntries>.ForError(new Error(Errors.Unavailable))));
        await using var provider = AProvider(services => services.AddSingleton(writer));
        await using var scope = provider.CreateAsyncScope();
        var queries = scope.ServiceProvider.GetRequiredKeyedService<IRegistryQueries>(TenantKey.None);

        // Act
        await queries.GetChannelsAsync(Ct);
        await queries.GetMessageTypesAsync(Ct);

        // Assert
        Assert.Equal(
            [(2910, "503"), (2912, "503")],
            RecordsOf(provider).Select(static record => (record.Id.Id, record.GetStructuredStateValue("ErrorCode"))));
    }

    [Fact]
    public async Task GetChannelHistoryAsync_Success_LogsOneDebugRecordWithTheCountTheChannelAndTheTenantOfTheKey()
    {
        // Arrange
        await using var provider = AProvider();
        await using var scope = provider.CreateAsyncScope();

        // Act
        await scope.ServiceProvider.GetRequiredKeyedService<IRegistryQueries>(_t1).GetChannelHistoryAsync(TestChannel.Id, Ct);

        // Assert
        var record = Assert.Single(RecordsOf(provider));
        Assert.Equal(
            (LogLevel.Debug, 2913, "0", "Test", "t1"),
            (record.Level, record.Id.Id, record.GetStructuredStateValue("Count"), record.GetStructuredStateValue("Channel"),
                record.GetStructuredStateValue("Tenant")));
    }

    [Fact]
    public async Task GetChannelHistoryAsync_ChannelThatIsNotRegistered_LogsOneRecordWithTheErrorType()
    {
        // Arrange
        const string unknown = "Unknown";
        await using var provider = AProvider();
        await using var scope = provider.CreateAsyncScope();

        // Act
        await scope.ServiceProvider.GetRequiredKeyedService<IRegistryQueries>(TenantKey.None).GetChannelHistoryAsync(new ChannelId(unknown), Ct);

        // Assert
        var record = Assert.Single(RecordsOf(provider));
        Assert.Equal(
            (2914, Errors.NotFound.Code.ToString(CultureInfo.InvariantCulture), unknown),
            (record.Id.Id, record.GetStructuredStateValue("ErrorCode"), record.GetStructuredStateValue("Channel")));
    }

    private static ServiceProvider AProvider(Action<IServiceCollection>? addServices = null)
    {
        var services = TestHost.CreateServices();
        RegistryTestHost.AddRegistry(services);
        addServices?.Invoke(services);
        return services.Build();
    }

    private static IEnumerable<FakeLogRecord> RecordsOf(IServiceProvider provider) =>
        provider.LogRecords().Where(static record => record.Id.Id is >= 2909 and <= 2914);
}
