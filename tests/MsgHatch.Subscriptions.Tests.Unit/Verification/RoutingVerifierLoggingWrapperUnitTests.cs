using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Subscriptions.Tests.Unit.TestTypes;
using MsgHatch.Subscriptions.Verification;
using NSubstitute;

namespace MsgHatch.Subscriptions.Tests.Unit.Verification;

/// <summary>
/// The standard logging wrapper tests for <see cref="IRoutingVerifier"/>, through the verifier of <c>UseSubscriptions</c>
/// with the in-memory store and the scenario, or with a store substitute that fails.
/// </summary>
public sealed class RoutingVerifierLoggingWrapperUnitTests
{
    private static readonly TenantKey _t1 = TenantKey.For(new TenantId("t1"));

    private static readonly RoutingVerificationRequest _request = new([new UserId("1")], ["en", "hu"]);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<int, LogLevel> LevelRows => new()
    {
        { Errors.Unavailable.Code, LogLevel.Warning },
        { Errors.Conflict.Code, LogLevel.Error },
        { Errors.OperationCancelled.Code, LogLevel.Information },
    };

    [Fact]
    public async Task VerifyAsync_Success_ReturnsTheReportAndLogsADebugRecordWithTheCountsAndNoTenant()
    {
        // Arrange
        await using var provider = ManagerHost.Build(TestHost.NewFakeTime());
        await provider.SeedScenarioAsync();
        await using var scope = provider.CreateAsyncScope();

        // Act
        var report = await scope.ServiceProvider.GetRequiredService<IRoutingVerifier>().VerifyAsync(_request, Ct);

        // Assert
        var record = Assert.Single(VerifierRecords(provider));
        Assert.Equal(
            (LogLevel.Debug, 1136, report.Value.Checks.Count.ToString(CultureInfo.InvariantCulture), report.Value.Passed.ToString(CultureInfo.InvariantCulture), (string?)null),
            (record.Level, record.Id.Id, record.GetStructuredStateValue("CheckCount"), record.GetStructuredStateValue("PassedCount"),
                record.GetStructuredStateValue("Tenant")));
    }

    [Fact]
    public async Task VerifyAsync_ErrorOfTheStore_ReturnsItAndLogsAWarningWithItsEventId()
    {
        // Arrange
        var error = new Error(Errors.Unavailable);
        await using var provider = ManagerHost.Build(TestHost.NewFakeTime(), store: _ => AFailingStore(error));
        await using var scope = provider.CreateAsyncScope();

        // Act
        var returned = await scope.ServiceProvider.GetRequiredService<IRoutingVerifier>().VerifyAsync(_request, Ct);

        // Assert
        var record = Assert.Single(VerifierRecords(provider));
        Assert.Equal((true, LogLevel.Warning, 1137), (ReferenceEquals(error, returned.Error), record.Level, record.Id.Id));
    }

    [Theory]
    [MemberData(nameof(LevelRows))]
    public async Task VerifyAsync_ErrorOfTheStore_LogsTheLevelOfTheError(int errorCode, LogLevel level)
    {
        // Arrange
        var errorType = new[] { Errors.Unavailable, Errors.Conflict, Errors.OperationCancelled }.Single(type => type.Code == errorCode);
        await using var provider = ManagerHost.Build(TestHost.NewFakeTime(), store: _ => AFailingStore(new Error(errorType)));
        await using var scope = provider.CreateAsyncScope();

        // Act
        await scope.ServiceProvider.GetRequiredService<IRoutingVerifier>().VerifyAsync(_request, Ct);

        // Assert
        var record = Assert.Single(VerifierRecords(provider));
        Assert.Equal((errorCode, level, 1137), (errorCode, record.Level, record.Id.Id));
    }

    [Fact]
    public async Task VerifyAsync_VerifierOfAKey_LogsTheTenantOfTheKey()
    {
        // Arrange
        await using var provider = ManagerHost.Build(TestHost.NewFakeTime());
        await using var scope = provider.CreateAsyncScope();

        // Act
        await scope.ServiceProvider.GetRequiredKeyedService<IRoutingVerifier>(_t1).VerifyAsync(_request, Ct);

        // Assert
        Assert.Equal("t1", Assert.Single(VerifierRecords(provider)).GetStructuredStateValue("Tenant"));
    }

    [Fact]
    public async Task UseSubscriptions_EnableLoggingFalse_RegistersNoLoggingWrapperOfTheVerifier()
    {
        // Arrange
        await using var provider = ManagerHost.Build(TestHost.NewFakeTime(), enableLogging: false);
        await using var scope = provider.CreateAsyncScope();

        // Act
        await scope.ServiceProvider.GetRequiredService<IRoutingVerifier>().VerifyAsync(_request, Ct);

        // Assert
        Assert.Empty(VerifierRecords(provider));
    }

    [Fact]
    public async Task VerifyAsync_ProfilesWithEmailAddresses_NoLogRecordContainsAnAddress()
    {
        // Arrange
        await using var provider = ManagerHost.Build(TestHost.NewFakeTime());
        await provider.SeedScenarioAsync();
        await using var scope = provider.CreateAsyncScope();

        // Act
        var report = await scope.ServiceProvider.GetRequiredService<IRoutingVerifier>()
            .VerifyAsync(new RoutingVerificationRequest([new UserId("1"), new UserId("3")], ["en", "hu"]), Ct);

        // Assert
        Assert.Equal((true, false), (report.Value.Passed > 0, provider.LogRecords().Any(static record => record.ContainsText("@"))));
    }

    private static List<FakeLogRecord> VerifierRecords(IServiceProvider provider) =>
        [.. provider.LogRecords().Where(static record => record.Id.Id is 1136 or 1137)];

    private static ISubscriptionStore AFailingStore(Error error)
    {
        var store = Substitute.For<ISubscriptionStore>();
        store.QueryAsync(default!, default).ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<IReadOnlyList<Subscription>>.ForError(error)));
        return store;
    }
}
