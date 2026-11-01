using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Outbox;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using NSubstitute;

namespace MsgHatch.Domain.Tests.Unit.Outbox;

/// <summary>The standard logging wrapper tests for <see cref="IOutboxStore"/>.</summary>
public sealed class OutboxStoreLoggingWrapperUnitTests
{
    private const string _address = "client1@example.com";
    private static readonly TenantKey _t1 = TenantKey.For(new TenantId("t1"));

    [Fact]
    public async Task AddAsync_InnerSuccess_ReturnsTheResultUnchanged()
    {
        // Arrange
        await using var provider = BuildWith(AStore(error: null));
        var store = provider.GetRequiredKeyedService<IOutboxStore>(_t1);

        // Act
        var error = await store.AddAsync([AMessage()], TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(error);
    }

    [Fact]
    public async Task AddAsync_InnerError_ReturnsTheSameError()
    {
        // Arrange
        var expected = new Error(Errors.Unavailable);
        await using var provider = BuildWith(AStore(expected));
        var store = provider.GetRequiredKeyedService<IOutboxStore>(_t1);

        // Act
        var error = await store.AddAsync([AMessage()], TestContext.Current.CancellationToken);

        // Assert
        Assert.Same(expected, error);
    }

    [Fact]
    public async Task AddAsync_TransientError_LogsAWarningWithItsEventId()
    {
        // Arrange
        await using var provider = BuildWith(AStore(new Error(Errors.Unavailable)));
        var store = provider.GetRequiredKeyedService<IOutboxStore>(_t1);

        // Act
        await store.AddAsync([AMessage()], TestContext.Current.CancellationToken);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal((LogLevel.Warning, 1301), (record.Level, record.Id.Id));
    }

    [Fact]
    public async Task AddAsync_PermanentError_LogsAnErrorWithItsEventId()
    {
        // Arrange
        await using var provider = BuildWith(AStore(new Error(Errors.ValidationFailed)));
        var store = provider.GetRequiredKeyedService<IOutboxStore>(_t1);

        // Act
        await store.AddAsync([AMessage()], TestContext.Current.CancellationToken);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal((LogLevel.Error, 1301), (record.Level, record.Id.Id));
    }

    [Fact]
    public async Task AddAsync_CancelledError_LogsInformationWithItsEventId()
    {
        // Arrange
        await using var provider = BuildWith(AStore(new Error(Errors.OperationCancelled)));
        var store = provider.GetRequiredKeyedService<IOutboxStore>(_t1);

        // Act
        await store.AddAsync([AMessage()], TestContext.Current.CancellationToken);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal((LogLevel.Information, 1301), (record.Level, record.Id.Id));
    }

    [Fact]
    public async Task AddAsync_EmptyList_LogsADebugRecordWithACountOfZero()
    {
        // Arrange
        await using var provider = BuildWith(AStore(error: null));
        var store = provider.GetRequiredKeyedService<IOutboxStore>(_t1);

        // Act
        await store.AddAsync([], TestContext.Current.CancellationToken);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal((LogLevel.Debug, "0"), (record.Level, record.GetStructuredStateValue("Count")));
    }

    [Fact]
    public async Task AddAsync_Success_LogsADebugRecordWithItsEventId()
    {
        // Arrange
        await using var provider = BuildWith(AStore(error: null));
        var store = provider.GetRequiredKeyedService<IOutboxStore>(_t1);

        // Act
        await store.AddAsync([AMessage()], TestContext.Current.CancellationToken);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal((LogLevel.Debug, 1300), (record.Level, record.Id.Id));
    }

    [Fact]
    public async Task AddAsync_TenantStoreOfAKey_LogsTheTenantOfTheKey()
    {
        // Arrange
        await using var provider = BuildWith(AStore(error: null));
        var store = provider.GetRequiredKeyedService<IOutboxStore>(_t1);

        // Act
        await store.AddAsync([AMessage()], TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("t1", Assert.Single(provider.LogRecords()).GetStructuredStateValue("Tenant"));
    }

    [Fact]
    public async Task AddAsync_TestMessage_NoLogRecordContainsTheChannelAddress()
    {
        // Arrange
        await using var provider = BuildWith(AStore(error: null));
        var store = provider.GetRequiredKeyedService<IOutboxStore>(_t1);
        var message = TestMessages.AnOutgoingMessage(new TestEnvelope(_address), tenant: _t1.Tenant, flags: MessageFlags.Test);

        // Act
        await store.AddAsync([message], TestContext.Current.CancellationToken);

        // Assert
        Assert.NotEmpty(provider.LogRecords());
        Assert.DoesNotContain(provider.LogRecords(), static record => record.ContainsText(_address));
    }

    [Fact]
    public async Task UseOutboxStore_EnableLoggingFalse_RegistersNoLoggingWrapper()
    {
        // Arrange
        var inner = AStore(error: null);
        await using var provider = BuildWith(inner, enableLogging: false);

        // Act
        var store = provider.GetRequiredKeyedService<IOutboxStore>(_t1);

        // Assert
        Assert.Same(inner, store);
    }

    private static IOutboxStore AStore(Error? error)
    {
        var store = Substitute.For<IOutboxStore>();
        store.AddAsync(default!, default).ReturnsForAnyArgs(ValueTask.FromResult(error));
        return store;
    }

    private static OutgoingMessage<TestEnvelope> AMessage() => TestMessages.AnOutgoingMessage(tenant: _t1.Tenant);

    private static ServiceProvider BuildWith(IOutboxStore inner, bool enableLogging = true)
    {
        var services = TestHost.CreateServices();
        services.AddMsgHatch(o => o.EnableLogging = enableLogging).UseOutboxStore((_, _) => inner);
        return services.Build();
    }
}
