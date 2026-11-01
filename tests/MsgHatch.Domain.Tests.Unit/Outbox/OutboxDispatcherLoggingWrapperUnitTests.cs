using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Outbox;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using NSubstitute;

namespace MsgHatch.Domain.Tests.Unit.Outbox;

/// <summary>
/// The standard logging wrapper tests for <see cref="IOutboxDispatcher"/>. The dispatcher has no factory overload, so
/// the wrapper wraps the real dispatcher, and an outbox dispatch store substitute gives its results. The dispatcher is
/// not a tenant service and logs only counts, so the rows of the tenant and of the address do not apply.
/// </summary>
public sealed class OutboxDispatcherLoggingWrapperUnitTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task DispatchPendingAsync_Success_ReturnsTheSummaryAndLogsADebugRecordWithItsEventId()
    {
        // Arrange
        await using var provider = BuildWith(AStore(new ClaimResult<OutgoingMessage>([TestMessages.AnOutgoingMessage()], null)));

        // Act
        var summary = await provider.GetRequiredService<IOutboxDispatcher>().DispatchPendingAsync(10, Ct);

        // Assert
        var record = Assert.Single(provider.LogRecords(), static r => r.Id.Id == 1314);
        Assert.Equal((new DispatchSummary(1, 1, 0, 0), LogLevel.Debug, "1"), (summary.Value, record.Level, record.GetStructuredStateValue("Claimed")));
    }

    [Fact]
    public async Task DispatchPendingAsync_TransientError_ReturnsTheSameErrorAndLogsAWarningWithItsEventId()
    {
        // Arrange
        var expected = new Error(Errors.Unavailable);
        await using var provider = BuildWith(AStore(expected));

        // Act
        var summary = await provider.GetRequiredService<IOutboxDispatcher>().DispatchPendingAsync(10, Ct);

        // Assert
        Assert.Same(expected, summary.Error);
        Assert.Equal(LogLevel.Warning, Assert.Single(provider.LogRecords(), static r => r.Id.Id == 1315).Level);
    }

    [Fact]
    public async Task DispatchPendingAsync_PermanentError_LogsAnErrorWithItsEventId()
    {
        // Arrange
        await using var provider = BuildWith(AStore(new Error(Errors.ValidationFailed)));

        // Act
        await provider.GetRequiredService<IOutboxDispatcher>().DispatchPendingAsync(10, Ct);

        // Assert
        Assert.Equal(LogLevel.Error, Assert.Single(provider.LogRecords(), static r => r.Id.Id == 1315).Level);
    }

    [Fact]
    public async Task RunAsync_CancelledToken_LogsTheStartAndTheStopAtTheInformationLevel()
    {
        // Arrange
        await using var provider = BuildWith(AStore(new ClaimResult<OutgoingMessage>([], null)));
        using var stop = new CancellationTokenSource();
        await stop.CancelAsync();

        // Act
        await provider.GetRequiredService<IOutboxDispatcher>().RunAsync(stop.Token);

        // Assert
        Assert.Equal(
            [(LogLevel.Information, 1312), (LogLevel.Information, 1313)],
            provider.LogRecords().Where(static r => r.Id.Id is 1312 or 1313).Select(static r => (r.Level, r.Id.Id)));
    }

    [Fact]
    public async Task AddMsgHatch_EnableLoggingFalse_RegistersNoLoggingWrapperOfTheDispatcher()
    {
        // Arrange
        await using var provider = BuildWith(AStore(new Error(Errors.Unavailable)), enableLogging: false);

        // Act
        await provider.GetRequiredService<IOutboxDispatcher>().DispatchPendingAsync(10, Ct);

        // Assert
        Assert.DoesNotContain(provider.LogRecords(), static r => r.Id.Id is >= 1312 and <= 1315);
    }

    private static IOutboxDispatchStore AStore(ErrorOr<ClaimResult<OutgoingMessage>> claimed)
    {
        var store = Substitute.For<IOutboxDispatchStore>();
        store.ClaimDueAsync(default, default, default, default, Ct).ReturnsForAnyArgs(ValueTask.FromResult(claimed));
        return store;
    }

    private static ServiceProvider BuildWith(IOutboxDispatchStore store, bool enableLogging = true)
    {
        var services = TestHost.CreateServices();
        services.AddMsgHatch(o => o.EnableLogging = enableLogging)
            .UseOutboxStore(static (_, _) => Substitute.For<IOutboxStore>())
            .UseOutboxDispatchStore(_ => store)
            .UseTransport(static _ => TestTransports.For(TestChannel.Id));
        return services.Build();
    }
}
