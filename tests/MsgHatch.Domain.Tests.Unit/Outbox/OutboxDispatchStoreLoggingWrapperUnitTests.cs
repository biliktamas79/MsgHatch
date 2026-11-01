using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Outbox;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using NSubstitute;

namespace MsgHatch.Domain.Tests.Unit.Outbox;

/// <summary>
/// The standard logging wrapper tests for <see cref="IOutboxDispatchStore"/>. It is not a tenant service, and
/// <c>DeleteTestDataAsync</c> gets no message, so the rows of the tenant and of the address do not apply.
/// </summary>
public sealed class OutboxDispatchStoreLoggingWrapperUnitTests
{
    [Fact]
    public async Task DeleteTestDataAsync_InnerCount_ReturnsTheCountUnchanged()
    {
        // Arrange
        const int count = 3;
        await using var provider = BuildWith(AStore(count));
        var store = provider.GetRequiredService<IOutboxDispatchStore>();

        // Act
        var deleted = await store.DeleteTestDataAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(count, deleted.Value);
    }

    [Fact]
    public async Task DeleteTestDataAsync_InnerError_ReturnsTheSameError()
    {
        // Arrange
        var expected = new Error(Errors.Unavailable);
        await using var provider = BuildWith(AStore(expected));
        var store = provider.GetRequiredService<IOutboxDispatchStore>();

        // Act
        var deleted = await store.DeleteTestDataAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Same(expected, deleted.Error);
    }

    [Fact]
    public async Task DeleteTestDataAsync_TransientError_LogsAWarningWithItsEventId()
    {
        // Arrange
        await using var provider = BuildWith(AStore(new Error(Errors.Unavailable)));
        var store = provider.GetRequiredService<IOutboxDispatchStore>();

        // Act
        await store.DeleteTestDataAsync(TestContext.Current.CancellationToken);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal((LogLevel.Warning, 1303), (record.Level, record.Id.Id));
    }

    [Fact]
    public async Task DeleteTestDataAsync_PermanentError_LogsAnErrorWithItsEventId()
    {
        // Arrange
        await using var provider = BuildWith(AStore(new Error(Errors.ValidationFailed)));
        var store = provider.GetRequiredService<IOutboxDispatchStore>();

        // Act
        await store.DeleteTestDataAsync(TestContext.Current.CancellationToken);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal((LogLevel.Error, 1303), (record.Level, record.Id.Id));
    }

    [Fact]
    public async Task UseOutboxDispatchStore_EnableLoggingFalse_RegistersNoLoggingWrapper()
    {
        // Arrange
        var inner = AStore(0);
        await using var provider = BuildWith(inner, enableLogging: false);

        // Act
        var store = provider.GetRequiredService<IOutboxDispatchStore>();

        // Assert
        Assert.Same(inner, store);
    }

    private static IOutboxDispatchStore AStore(ErrorOr<int> result)
    {
        var store = Substitute.For<IOutboxDispatchStore>();
        store.DeleteTestDataAsync(default).ReturnsForAnyArgs(ValueTask.FromResult(result));
        return store;
    }

    private static ServiceProvider BuildWith(IOutboxDispatchStore inner, bool enableLogging = true)
    {
        var services = TestHost.CreateServices();
        services.AddMsgHatch(o => o.EnableLogging = enableLogging).UseOutboxDispatchStore(_ => inner);
        return services.Build();
    }
}
