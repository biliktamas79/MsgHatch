using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Pipeline;
using MsgHatch.Domain.Routing;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using NSubstitute;

namespace MsgHatch.Domain.Tests.Unit.Pipeline;

/// <summary>The standard logging wrapper tests for <see cref="IPayloadLoader{TEvent, TPayload}"/>.</summary>
public sealed class PayloadLoaderLoggingWrapperUnitTests
{
    private const string _address = "client1@example.com";
    private static readonly TenantKey _t1 = TenantKey.For(new TenantId("t1"));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task LoadAsync_InnerPayloads_ReturnsThemUnchanged()
    {
        // Arrange
        IReadOnlyList<TestEnvelope> payloads = [new TestEnvelope("first"), new TestEnvelope("second")];
        var inner = ALoader(ErrorOr<IReadOnlyList<TestEnvelope>>.ForValue(payloads));
        await using var provider = BuildWith(inner);
        await using var scope = provider.CreateAsyncScope();
        var message = AMessage();
        MessageRoute[] routes = [TestRoutes.ARoute(address: _address)];

        // Act
        var result = await LoadAsync(scope.ServiceProvider, message, routes);

        // Assert
        Assert.Same(payloads, result.Value);
        _ = inner.Received(1).LoadAsync(message, routes, Ct);
    }

    [Fact]
    public async Task LoadAsync_InnerError_ReturnsTheSameError()
    {
        // Arrange
        var error = new Error(Errors.Unavailable);
        var inner = ALoader(error);
        await using var provider = BuildWith(inner);
        await using var scope = provider.CreateAsyncScope();
        var message = AMessage();
        MessageRoute[] routes = [TestRoutes.ARoute(address: _address)];

        // Act
        var result = await LoadAsync(scope.ServiceProvider, message, routes);

        // Assert
        Assert.Same(error, result.Error);
        _ = inner.Received(1).LoadAsync(message, routes, Ct);
    }

    [Fact]
    public async Task LoadAsync_TransientError_LogsAWarningWithItsEventId()
    {
        // Arrange
        var inner = ALoader(new Error(Errors.Unavailable));
        await using var provider = BuildWith(inner);
        await using var scope = provider.CreateAsyncScope();
        var message = AMessage();
        MessageRoute[] routes = [TestRoutes.ARoute(address: _address)];

        // Act
        await LoadAsync(scope.ServiceProvider, message, routes);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal((LogLevel.Warning, 1003), (record.Level, record.Id.Id));
        _ = inner.Received(1).LoadAsync(message, routes, Ct);
    }

    [Fact]
    public async Task LoadAsync_PermanentError_LogsAnErrorWithItsEventId()
    {
        // Arrange
        var inner = ALoader(new Error(Errors.ValidationFailed));
        await using var provider = BuildWith(inner);
        await using var scope = provider.CreateAsyncScope();
        var message = AMessage();
        MessageRoute[] routes = [TestRoutes.ARoute(address: _address)];

        // Act
        await LoadAsync(scope.ServiceProvider, message, routes);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal((LogLevel.Error, 1003), (record.Level, record.Id.Id));
        _ = inner.Received(1).LoadAsync(message, routes, Ct);
    }

    [Fact]
    public async Task LoadAsync_TenantServiceOfAKey_LogsTheTenantOfTheKey()
    {
        // Arrange
        var inner = ALoader(ErrorOr<IReadOnlyList<TestEnvelope>>.ForValue([]));
        await using var provider = BuildWith(inner);
        await using var scope = provider.CreateAsyncScope();
        var message = AMessage();
        MessageRoute[] routes = [TestRoutes.ARoute(address: _address)];

        // Act
        await LoadAsync(scope.ServiceProvider, message, routes);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal(("t1", 1002), (record.GetStructuredStateValue("Tenant"), record.Id.Id));
        _ = inner.Received(1).LoadAsync(message, routes, Ct);
    }

    [Fact]
    public async Task LoadAsync_TestMessage_NoLogRecordContainsTheChannelAddress()
    {
        // Arrange
        var inner = ALoader(ErrorOr<IReadOnlyList<TestEnvelope>>.ForValue([new TestEnvelope(_address)]));
        await using var provider = BuildWith(inner);
        await using var scope = provider.CreateAsyncScope();
        var message = AMessage();
        MessageRoute[] routes = [TestRoutes.ARoute(address: _address)];

        // Act
        await LoadAsync(scope.ServiceProvider, message, routes);

        // Assert
        Assert.NotEmpty(provider.LogRecords());
        Assert.DoesNotContain(provider.LogRecords(), static record => record.ContainsText(_address));
        _ = inner.Received(1).LoadAsync(message, routes, Ct);
    }

    [Fact]
    public async Task UsePayloadLoader_EnableLoggingFalse_RegistersNoLoggingWrapper()
    {
        // Arrange
        var inner = ALoader(ErrorOr<IReadOnlyList<TestEnvelope>>.ForValue([]));
        await using var provider = BuildWith(inner, enableLogging: false);
        await using var scope = provider.CreateAsyncScope();

        // Act
        var loader = scope.ServiceProvider.GetRequiredKeyedService<IPayloadLoader<TestEvent, TestEnvelope>>(_t1);

        // Assert
        Assert.Same(inner, loader);
    }

    private static IPayloadLoader<TestEvent, TestEnvelope> ALoader(ErrorOr<IReadOnlyList<TestEnvelope>> result)
    {
        var loader = Substitute.For<IPayloadLoader<TestEvent, TestEnvelope>>();
        loader.LoadAsync(default!, default, default).ReturnsForAnyArgs(ValueTask.FromResult(result));
        return loader;
    }

    private static ReceivedMessage<TestEvent> AMessage() =>
        TestMessages.AReceivedMessage(new TestEvent(_address), _t1.Tenant, flags: MessageFlags.Test);

    private static ValueTask<ErrorOr<IReadOnlyList<TestEnvelope>>> LoadAsync(
        IServiceProvider scope, ReceivedMessage<TestEvent> message, MessageRoute[] routes) =>
        scope.GetRequiredKeyedService<IPayloadLoader<TestEvent, TestEnvelope>>(_t1).LoadAsync(message, routes, Ct);

    private static ServiceProvider BuildWith(IPayloadLoader<TestEvent, TestEnvelope> inner, bool enableLogging = true)
    {
        var services = TestHost.CreateServices();
        services.AddMsgHatch(o => o.EnableLogging = enableLogging)
            .UseRouter(TestOpenRouter.Factory)
            .AddMessage("TestEvent", 2, TestJsonContext.Default.TestEvent, m => m.UsePayloadLoader((_, _) => inner));
        return services.Build();
    }
}
