using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Pipeline;
using MsgHatch.Domain.Routing;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using NSubstitute;

namespace MsgHatch.Domain.Tests.Unit.Pipeline;

/// <summary>
/// The standard logging wrapper tests for <see cref="INotificationService"/>. The library registers the service, so
/// the inner results come from the real pipeline with substitutes. It is not a tenant service, and it logs the
/// tenant of the message context.
/// </summary>
public sealed class NotificationServiceLoggingWrapperUnitTests
{
    private const string _address = "client1@example.com";
    private static readonly TenantId _tenant = new("t1");

    private readonly FakeTimeProvider _time = TestHost.NewFakeTime();
    private readonly PipelineDoubles _doubles = new();

    [Fact]
    public async Task ProcessAsync_InnerError_ReturnsTheSameError()
    {
        // Arrange
        var error = new Error(Errors.Unavailable);
        RouterGives(error);
        await using var provider = _doubles.Build(_time);

        // Act
        var result = await ProcessAsync(provider, new MessageContext(Guid.NewGuid(), _tenant));

        // Assert
        Assert.Same(error, result.Error);
    }

    [Fact]
    public async Task ProcessAsync_TransientError_LogsAWarningWithItsEventId()
    {
        // Arrange
        RouterGives(new Error(Errors.Unavailable));
        await using var provider = _doubles.Build(_time);

        // Act
        await ProcessAsync(provider, new MessageContext(Guid.NewGuid(), _tenant));

        // Assert
        Assert.Equal(LogLevel.Warning, Assert.Single(provider.LogRecords(), static r => r.Id.Id == 1005).Level);
    }

    [Fact]
    public async Task ProcessAsync_PermanentError_LogsAnErrorWithItsEventId()
    {
        // Arrange
        RouterGives(new Error(Errors.ValidationFailed));
        await using var provider = _doubles.Build(_time);

        // Act
        await ProcessAsync(provider, new MessageContext(Guid.NewGuid(), _tenant));

        // Assert
        Assert.Equal(LogLevel.Error, Assert.Single(provider.LogRecords(), static r => r.Id.Id == 1005).Level);
    }

    [Fact]
    public async Task ProcessAsync_Success_LogsTheTenantOfTheContextAndTheCounts()
    {
        // Arrange
        _doubles.Routes = [TestRoutes.ARoute("u1"), TestRoutes.ARoute("u2")];
        await using var provider = _doubles.Build(_time);

        // Act
        await ProcessAsync(provider, new MessageContext(Guid.NewGuid(), _tenant));

        // Assert
        var record = Assert.Single(provider.LogRecords(), static r => r.Id.Id == 1004);
        Assert.Equal(
            (LogLevel.Debug, "t1", "2", "0"),
            (record.Level, record.GetStructuredStateValue("Tenant"), record.GetStructuredStateValue("Stored"),
                record.GetStructuredStateValue("Failed")));
    }

    [Fact]
    public async Task ProcessAsync_TestMessage_NoLogRecordContainsTheChannelAddress()
    {
        // Arrange
        _doubles.Routes = [TestRoutes.ARoute("u1", address: _address)];
        await using var provider = _doubles.Build(_time);

        // Act
        await ProcessAsync(provider, new MessageContext(Guid.NewGuid(), _tenant, MessageFlags.Test));

        // Assert
        Assert.NotEmpty(provider.LogRecords());
        Assert.DoesNotContain(provider.LogRecords(), static record => record.ContainsText(_address));
    }

    [Fact]
    public async Task ProcessAsync_EnableLoggingFalse_NoLoggingWrapperWritesALogRecord()
    {
        // Arrange
        _doubles.Routes = [TestRoutes.ARoute("u1")];
        await using var provider = _doubles.Build(_time, enableLogging: false);

        // Act
        await ProcessAsync(provider, new MessageContext(Guid.NewGuid(), _tenant));

        // Assert
        Assert.Empty(provider.LogRecords());
    }

    [Fact]
    public async Task PreviewAsync_Success_LogsADebugRecordWithItsEventId()
    {
        // Arrange
        await using var provider = _doubles.Build(_time);

        // Act
        await PreviewAsync(provider, [TestRoutes.ARoute("u1")]);

        // Assert
        Assert.Equal(LogLevel.Debug, Assert.Single(provider.LogRecords(), static r => r.Id.Id == 1006).Level);
    }

    [Fact]
    public async Task PreviewAsync_PermanentError_LogsAnErrorWithItsEventId()
    {
        // Arrange
        RouterGives(new Error(Errors.ValidationFailed));
        await using var provider = _doubles.Build(_time);

        // Act
        await PreviewAsync(provider, routes: null);

        // Assert
        Assert.Equal(LogLevel.Error, Assert.Single(provider.LogRecords(), static r => r.Id.Id == 1007).Level);
    }

    private void RouterGives(Error error) =>
        _doubles.Router(TenantKey.For(_tenant)).RouteAsync(default!, default)
            .ReturnsForAnyArgs(ValueTask.FromResult<ErrorOr<IReadOnlyList<MessageRoute>>>(error));

    private static async Task<ErrorOr<IReadOnlyList<RouteResult>>> PreviewAsync(
        IServiceProvider provider, IReadOnlyList<MessageRoute>? routes)
    {
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<INotificationService>()
            .PreviewAsync(new TestEvent("text"), new MessageContext(Guid.NewGuid(), _tenant), routes, TestContext.Current.CancellationToken);
    }

    private static async Task<ErrorOr<NotificationResult>> ProcessAsync(IServiceProvider provider, MessageContext context)
    {
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<INotificationService>()
            .ProcessAsync(new TestEvent("text"), context, TestContext.Current.CancellationToken);
    }
}
