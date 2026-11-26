using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Routing;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using NSubstitute;

namespace MsgHatch.Domain.Tests.Unit.Pipeline;

/// <summary>The standard logging wrapper tests for <see cref="IMessageRouter{TEvent}"/>.</summary>
public sealed class MessageRouterLoggingWrapperUnitTests
{
    private const string _address = "client1@example.com";
    private static readonly TenantKey _t1 = TenantKey.For(new TenantId("t1"));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task RouteAsync_InnerRoutes_ReturnsThemUnchanged()
    {
        // Arrange
        IReadOnlyList<MessageRoute> routes = [TestRoutes.ARoute("u1"), TestRoutes.ARoute("u2")];
        var inner = ARouter(ErrorOr<IReadOnlyList<MessageRoute>>.ForValue(routes));
        await using var provider = BuildWith(inner);
        await using var scope = provider.CreateAsyncScope();
        var message = AMessage();

        // Act
        var result = await RouteAsync(scope.ServiceProvider, message);

        // Assert
        Assert.Same(routes, result.Value);
        _ = inner.Received(1).RouteAsync(message, Ct);
    }

    [Fact]
    public async Task RouteAsync_InnerError_ReturnsTheSameError()
    {
        // Arrange
        var error = new Error(Errors.Unavailable);
        var inner = ARouter(error);
        await using var provider = BuildWith(inner);
        await using var scope = provider.CreateAsyncScope();
        var message = AMessage();

        // Act
        var result = await RouteAsync(scope.ServiceProvider, message);

        // Assert
        Assert.Same(error, result.Error);
        _ = inner.Received(1).RouteAsync(message, Ct);
    }

    [Fact]
    public async Task RouteAsync_TransientError_LogsAWarningWithItsEventId()
    {
        // Arrange
        var inner = ARouter(new Error(Errors.Unavailable));
        await using var provider = BuildWith(inner);
        await using var scope = provider.CreateAsyncScope();
        var message = AMessage();

        // Act
        await RouteAsync(scope.ServiceProvider, message);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal((LogLevel.Warning, 1001), (record.Level, record.Id.Id));
        _ = inner.Received(1).RouteAsync(message, Ct);
    }

    [Fact]
    public async Task RouteAsync_PermanentError_LogsAnErrorWithItsEventId()
    {
        // Arrange
        var inner = ARouter(new Error(Errors.ValidationFailed));
        await using var provider = BuildWith(inner);
        await using var scope = provider.CreateAsyncScope();
        var message = AMessage();

        // Act
        await RouteAsync(scope.ServiceProvider, message);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal((LogLevel.Error, 1001), (record.Level, record.Id.Id));
        _ = inner.Received(1).RouteAsync(message, Ct);
    }

    [Fact]
    public async Task RouteAsync_TenantServiceOfAKey_LogsTheTenantOfTheKey()
    {
        // Arrange
        var inner = ARouter(ErrorOr<IReadOnlyList<MessageRoute>>.ForValue([]));
        await using var provider = BuildWith(inner);
        await using var scope = provider.CreateAsyncScope();
        var message = AMessage();

        // Act
        await RouteAsync(scope.ServiceProvider, message);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal(("t1", 1000), (record.GetStructuredStateValue("Tenant"), record.Id.Id));
        _ = inner.Received(1).RouteAsync(message, Ct);
    }

    [Fact]
    public async Task RouteAsync_TestMessage_NoLogRecordContainsTheChannelAddress()
    {
        // Arrange
        var inner = ARouter(ErrorOr<IReadOnlyList<MessageRoute>>.ForValue([TestRoutes.ARoute(address: _address)]));
        await using var provider = BuildWith(inner);
        await using var scope = provider.CreateAsyncScope();
        var message = AMessage();

        // Act
        await RouteAsync(scope.ServiceProvider, message);

        // Assert
        Assert.NotEmpty(provider.LogRecords());
        Assert.DoesNotContain(provider.LogRecords(), static record => record.ContainsText(_address));
        _ = inner.Received(1).RouteAsync(message, Ct);
    }

    [Fact]
    public async Task WithRouter_EnableLoggingFalse_RegistersNoLoggingWrapper()
    {
        // Arrange
        var inner = ARouter(ErrorOr<IReadOnlyList<MessageRoute>>.ForValue([]));
        await using var provider = BuildWith(inner, enableLogging: false);
        await using var scope = provider.CreateAsyncScope();

        // Act
        var router = scope.ServiceProvider.GetRequiredKeyedService<IMessageRouter<TestEvent>>(_t1);

        // Assert
        Assert.Same(inner, router);
    }

    private static IMessageRouter<TestEvent> ARouter(ErrorOr<IReadOnlyList<MessageRoute>> result)
    {
        var router = Substitute.For<IMessageRouter<TestEvent>>();
        router.RouteAsync(default!, default).ReturnsForAnyArgs(ValueTask.FromResult(result));
        return router;
    }

    private static ReceivedMessage<TestEvent> AMessage() =>
        TestMessages.AReceivedMessage(new TestEvent(_address), _t1.Tenant, flags: MessageFlags.Test);

    private static ValueTask<ErrorOr<IReadOnlyList<MessageRoute>>> RouteAsync(IServiceProvider scope, ReceivedMessage<TestEvent> message) =>
        scope.GetRequiredKeyedService<IMessageRouter<TestEvent>>(_t1).RouteAsync(message, Ct);

    private static ServiceProvider BuildWith(IMessageRouter<TestEvent> inner, bool enableLogging = true)
    {
        var services = TestHost.CreateServices();
        services.AddMsgHatch(o => o.EnableLogging = enableLogging)
            .AddMessage("TestEvent", 2, TestJsonContext.Default.TestEvent, m => m.WithRouter((_, _) => inner));
        return services.Build();
    }
}
