using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Inbox;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using NSubstitute;

namespace MsgHatch.Domain.Tests.Unit.Inbox;

/// <summary>The standard logging wrapper tests for <see cref="IInboxStore"/>. A received message has no channel address, so the row of the address checks the event.</summary>
public sealed class InboxStoreLoggingWrapperUnitTests
{
    private const string _eventText = "client1@example.com";
    private static readonly TenantKey _t1 = TenantKey.For(new TenantId("t1"));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AddAsync_InnerError_ReturnsTheSameError()
    {
        // Arrange
        var expected = new Error(Errors.Unavailable);
        await using var provider = BuildWith(AStore(expected));
        var store = provider.GetRequiredKeyedService<IInboxStore>(_t1);

        // Act
        var error = await store.AddAsync(AMessage(), Ct);

        // Assert
        Assert.Same(expected, error);
    }

    [Fact]
    public async Task AddAsync_TransientError_LogsAWarningWithItsEventId()
    {
        // Arrange
        await using var provider = BuildWith(AStore(new Error(Errors.Unavailable)));
        var store = provider.GetRequiredKeyedService<IInboxStore>(_t1);

        // Act
        await store.AddAsync(AMessage(), Ct);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal((LogLevel.Warning, 1201), (record.Level, record.Id.Id));
    }

    [Fact]
    public async Task AddAsync_PermanentError_LogsAnErrorWithItsEventId()
    {
        // Arrange
        await using var provider = BuildWith(AStore(new Error(Errors.ValidationFailed)));
        var store = provider.GetRequiredKeyedService<IInboxStore>(_t1);

        // Act
        await store.AddAsync(AMessage(), Ct);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal((LogLevel.Error, 1201), (record.Level, record.Id.Id));
    }

    [Fact]
    public async Task AddAsync_Success_LogsTheTenantOfTheKeyWithItsEventId()
    {
        // Arrange
        await using var provider = BuildWith(AStore(error: null));
        var store = provider.GetRequiredKeyedService<IInboxStore>(_t1);

        // Act
        var error = await store.AddAsync(AMessage(), Ct);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal(((Error?)null, LogLevel.Debug, 1200, "t1"), (error, record.Level, record.Id.Id, record.GetStructuredStateValue("Tenant")));
    }

    [Fact]
    public async Task AddAsync_TestMessage_NoLogRecordContainsTheEvent()
    {
        // Arrange
        await using var provider = BuildWith(AStore(error: null));
        var store = provider.GetRequiredKeyedService<IInboxStore>(_t1);

        // Act
        await store.AddAsync(TestMessages.AReceivedMessage(new TestEvent(_eventText), _t1.Tenant, flags: MessageFlags.Test), Ct);

        // Assert
        Assert.NotEmpty(provider.LogRecords());
        Assert.DoesNotContain(provider.LogRecords(), static record => record.ContainsText(_eventText));
    }

    [Fact]
    public async Task UseInboxStore_EnableLoggingFalse_RegistersNoLoggingWrapper()
    {
        // Arrange
        var inner = AStore(error: null);
        await using var provider = BuildWith(inner, enableLogging: false);

        // Act
        var store = provider.GetRequiredKeyedService<IInboxStore>(_t1);

        // Assert
        Assert.Same(inner, store);
    }

    private static IInboxStore AStore(Error? error)
    {
        var store = Substitute.For<IInboxStore>();
        store.AddAsync(default!, default).ReturnsForAnyArgs(ValueTask.FromResult(error));
        return store;
    }

    private static ReceivedMessage<TestEvent> AMessage() => TestMessages.AReceivedMessage(new TestEvent("text"), _t1.Tenant);

    private static ServiceProvider BuildWith(IInboxStore inner, bool enableLogging = true)
    {
        var services = TestHost.CreateServices();
        services.AddMsgHatch(o => o.EnableLogging = enableLogging).UseInboxStore((_, _) => inner);
        return services.Build();
    }
}
