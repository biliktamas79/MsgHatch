using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Inbox;
using MsgHatch.Domain.Outbox;
using MsgHatch.Domain.Persistence;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using NSubstitute;

namespace MsgHatch.Domain.Tests.Unit.Persistence;

/// <summary>The standard logging wrapper tests for <see cref="ITenantSource"/>.</summary>
public sealed class TenantSourceLoggingWrapperUnitTests
{
    private const string _secretTenant = "tenant-of-client1@example.com";

    [Fact]
    public async Task GetTenantsAsync_TransientError_ReturnsTheSameErrorAndLogsAWarningWithItsEventId()
    {
        // Arrange
        var expected = new Error(Errors.Unavailable);
        await using var provider = BuildWith(ASourceOf(expected));

        // Act
        var result = await provider.GetRequiredService<ITenantSource>().GetTenantsAsync(Ct);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal((expected, LogLevel.Warning, 1501), (result.Error, record.Level, record.Id.Id));
    }

    [Fact]
    public async Task GetTenantsAsync_PermanentError_LogsAnErrorWithItsEventId()
    {
        // Arrange
        await using var provider = BuildWith(ASourceOf(new Error(Errors.SerializationFailed)));

        // Act
        await provider.GetRequiredService<ITenantSource>().GetTenantsAsync(Ct);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal((LogLevel.Error, 1501), (record.Level, record.Id.Id));
    }

    [Fact]
    public async Task GetTenantsAsync_Cancelled_LogsAnInformationRecordWithItsEventId()
    {
        // Arrange
        await using var provider = BuildWith(ASourceOf(new Error(Errors.OperationCancelled)));

        // Act
        await provider.GetRequiredService<ITenantSource>().GetTenantsAsync(Ct);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal((LogLevel.Information, 1501), (record.Level, record.Id.Id));
    }

    [Fact]
    public async Task GetTenantsAsync_TwoTenants_ReturnsThemUnchangedAndLogsTheirCountAndNoTenantId()
    {
        // Arrange
        IReadOnlyList<TenantId> tenants = [new TenantId(_secretTenant), new TenantId("t2")];
        await using var provider = BuildWith(ASourceOf(tenants));

        // Act
        var result = await provider.GetRequiredService<ITenantSource>().GetTenantsAsync(Ct);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Same(tenants, result.Value);
        Assert.Equal((LogLevel.Debug, 1500, "2", false), (record.Level, record.Id.Id, record.GetStructuredStateValue("TenantCount"), record.ContainsText(_secretTenant)));
    }

    [Fact]
    public async Task UseStorageForEachTenant_EnableLoggingFalse_RegistersNoLoggingWrapper()
    {
        // Arrange
        var inner = ASourceOf([]);
        await using var provider = BuildWith(inner, enableLogging: false);

        // Act
        var source = provider.GetRequiredService<ITenantSource>();

        // Assert
        Assert.Same(inner, source);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ITenantSource ASourceOf(ErrorOr<IReadOnlyList<TenantId>> result)
    {
        var source = Substitute.For<ITenantSource>();
        source.GetTenantsAsync(default).ReturnsForAnyArgs(ValueTask.FromResult(result));
        return source;
    }

    private static ITenantSource ASourceOf(Error error) => ASourceOf(ErrorOr<IReadOnlyList<TenantId>>.ForError(error));

    private static ITenantSource ASourceOf(IReadOnlyList<TenantId> tenants) => ASourceOf(ErrorOr<IReadOnlyList<TenantId>>.ForValue(tenants));

    private static ServiceProvider BuildWith(ITenantSource inner, bool enableLogging = true)
    {
        var services = TestHost.CreateServices();
        services.AddMsgHatch(o => o.EnableLogging = enableLogging).UseStorageForEachTenant(
            _ => inner,
            static (_, key) => new InMemoryInboxStore().For(key), static (_, _) => new InMemoryInboxStore(),
            static (_, key) => new InMemoryOutboxStore().For(key), static (_, _) => new InMemoryOutboxStore());
        return services.Build();
    }
}
