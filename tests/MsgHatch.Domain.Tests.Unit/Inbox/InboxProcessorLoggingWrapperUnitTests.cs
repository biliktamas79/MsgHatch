using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Inbox;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using NSubstitute;

namespace MsgHatch.Domain.Tests.Unit.Inbox;

/// <summary>
/// The standard logging wrapper tests for <see cref="IInboxProcessor"/>. The processor has no factory overload, so the
/// wrapper wraps the real processor, and an inbox dispatch store substitute gives its results. The processor is not a
/// tenant service and logs only counts, so the rows of the tenant and of the address do not apply.
/// </summary>
public sealed class InboxProcessorLoggingWrapperUnitTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ProcessPendingAsync_Success_ReturnsTheSummaryAndLogsADebugRecordWithItsEventId()
    {
        // Arrange
        await using var provider = BuildWith(AStore(new ClaimResult<ReceivedMessage>([], null)));

        // Act
        var summary = await provider.GetRequiredService<IInboxProcessor>().ProcessPendingAsync(10, Ct);

        // Assert
        var record = Assert.Single(provider.LogRecords(), static r => r.Id.Id == 1210);
        Assert.Equal((new DispatchSummary(0, 0, 0, 0), LogLevel.Debug), (summary.Value, record.Level));
    }

    [Fact]
    public async Task ProcessPendingAsync_TransientError_ReturnsTheSameErrorAndLogsAWarningWithItsEventId()
    {
        // Arrange
        var expected = new Error(Errors.Unavailable);
        await using var provider = BuildWith(AStore(expected));

        // Act
        var summary = await provider.GetRequiredService<IInboxProcessor>().ProcessPendingAsync(10, Ct);

        // Assert
        Assert.Same(expected, summary.Error);
        Assert.Equal(LogLevel.Warning, Assert.Single(provider.LogRecords(), static r => r.Id.Id == 1211).Level);
    }

    [Fact]
    public async Task ProcessPendingAsync_PermanentError_LogsAnErrorWithItsEventId()
    {
        // Arrange
        await using var provider = BuildWith(AStore(new Error(Errors.ValidationFailed)));

        // Act
        await provider.GetRequiredService<IInboxProcessor>().ProcessPendingAsync(10, Ct);

        // Assert
        Assert.Equal(LogLevel.Error, Assert.Single(provider.LogRecords(), static r => r.Id.Id == 1211).Level);
    }

    [Fact]
    public async Task RunAsync_CancelledToken_LogsTheStartAndTheStopAtTheInformationLevel()
    {
        // Arrange
        await using var provider = BuildWith(AStore(new ClaimResult<ReceivedMessage>([], null)));
        using var stop = new CancellationTokenSource();
        await stop.CancelAsync();

        // Act
        await provider.GetRequiredService<IInboxProcessor>().RunAsync(stop.Token);

        // Assert
        Assert.Equal(
            [(LogLevel.Information, 1208), (LogLevel.Information, 1209)],
            provider.LogRecords().Where(static r => r.Id.Id is 1208 or 1209).Select(static r => (r.Level, r.Id.Id)));
    }

    [Fact]
    public async Task AddMsgHatch_EnableLoggingFalse_RegistersNoLoggingWrapperOfTheProcessor()
    {
        // Arrange
        await using var provider = BuildWith(AStore(new Error(Errors.Unavailable)), enableLogging: false);

        // Act
        await provider.GetRequiredService<IInboxProcessor>().ProcessPendingAsync(10, Ct);

        // Assert
        Assert.DoesNotContain(provider.LogRecords(), static r => r.Id.Id is >= 1208 and <= 1211);
    }

    private static IInboxDispatchStore AStore(ErrorOr<ClaimResult<ReceivedMessage>> claimed)
    {
        var store = Substitute.For<IInboxDispatchStore>();
        store.ClaimDueAsync(default, default, default, Ct).ReturnsForAnyArgs(ValueTask.FromResult(claimed));
        return store;
    }

    private static ServiceProvider BuildWith(IInboxDispatchStore store, bool enableLogging = true)
    {
        var services = TestHost.CreateServices();
        services.AddMsgHatch(o => o.EnableLogging = enableLogging).UseInboxDispatchStore(_ => store);
        return services.Build();
    }
}
