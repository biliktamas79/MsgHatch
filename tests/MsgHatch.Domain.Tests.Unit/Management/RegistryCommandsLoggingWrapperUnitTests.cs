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
/// The standard logging wrapper tests for <see cref="IRegistryCommands"/> (D244, D283). The wrapper wraps the registry
/// manager of <see cref="RegistryTestHost"/> with a transport of the channel Test, and each test reads only the records of
/// the events 2915 and 2916.
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
        await using var provider = AProvider(checkError: null);
        await using var scope = provider.CreateAsyncScope();

        // Act
        await scope.ServiceProvider.GetRequiredKeyedService<IRegistryCommands>(TenantKey.None).CheckChannelAsync(new ChannelId("Unknown"), Ct);

        // Assert
        var record = Assert.Single(RecordsOf(provider));
        Assert.Equal(
            (2916, Errors.NotFound.Code.ToString(CultureInfo.InvariantCulture), "Unknown"),
            (record.Id.Id, record.GetStructuredStateValue("ErrorCode"), record.GetStructuredStateValue("Channel")));
    }

    private static ServiceProvider AProvider(Error? checkError)
    {
        var transport = Substitute.For<ITransport>();
        transport.Channel.Returns(TestChannel.Id);
        transport.CheckHealthAsync(Ct).ReturnsForAnyArgs(_ => ValueTask.FromResult(checkError));
        var services = TestHost.CreateServices();
        RegistryTestHost.AddRegistry(services).UseTransport(_ => transport);
        return services.Build();
    }

    private static IEnumerable<FakeLogRecord> RecordsOf(IServiceProvider provider) =>
        provider.LogRecords().Where(static record => record.Id.Id is 2915 or 2916);
}
