using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using NSubstitute;

namespace MsgHatch.Domain.Tests.Unit.Delivery;

/// <summary>
/// The standard logging wrapper tests for <see cref="ITransport"/>. A transport is not a tenant service, so the
/// wrapper logs the tenant of the message.
/// </summary>
public sealed class TransportLoggingWrapperUnitTests
{
    private const string _address = "client1@example.com";
    private const string _redirectAddress = "tester@example.com";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Channel_InnerChannel_ReturnsTheSameChannel()
    {
        // Arrange
        await using var provider = BuildWith(TestTransports.For(TestChannel.Id));

        // Act
        var transport = provider.GetRequiredService<ITransport>();

        // Assert
        Assert.Equal(TestChannel.Id, transport.Channel);
    }

    [Fact]
    public async Task SendAsync_InnerReceipt_ReturnsTheReceiptUnchanged()
    {
        // Arrange
        var receipt = new PublishReceipt("provider-7");
        var inner = TestTransports.For(TestChannel.Id, receipt);
        await using var provider = BuildWith(inner);
        var transport = provider.GetRequiredService<ITransport>();
        var message = TestMessages.AnOutgoingMessage();

        // Act
        var result = await transport.SendAsync(message, redirectTo: null, Ct);

        // Assert
        Assert.Equal(receipt, result.Value);
        _ = inner.Received(1).SendAsync(message, null, Ct);
    }

    [Fact]
    public async Task SendAsync_InnerError_ReturnsTheSameError()
    {
        // Arrange
        var expected = new Error(Errors.Unavailable);
        var inner = TestTransports.For(TestChannel.Id, expected);
        await using var provider = BuildWith(inner);
        var transport = provider.GetRequiredService<ITransport>();
        var message = TestMessages.AnOutgoingMessage();

        // Act
        var result = await transport.SendAsync(message, redirectTo: null, Ct);

        // Assert
        Assert.Same(expected, result.Error);
        _ = inner.Received(1).SendAsync(message, null, Ct);
    }

    [Fact]
    public async Task SendAsync_TransientError_LogsAWarningWithItsEventId()
    {
        // Arrange
        var inner = TestTransports.For(TestChannel.Id, new Error(Errors.RateLimited));
        await using var provider = BuildWith(inner);
        var transport = provider.GetRequiredService<ITransport>();
        var message = TestMessages.AnOutgoingMessage();

        // Act
        await transport.SendAsync(message, redirectTo: null, Ct);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal((LogLevel.Warning, 1351), (record.Level, record.Id.Id));
        _ = inner.Received(1).SendAsync(message, null, Ct);
    }

    [Fact]
    public async Task SendAsync_PermanentError_LogsAnErrorWithItsEventId()
    {
        // Arrange
        var inner = TestTransports.For(TestChannel.Id, new Error(Errors.Rejected));
        await using var provider = BuildWith(inner);
        var transport = provider.GetRequiredService<ITransport>();
        var message = TestMessages.AnOutgoingMessage();

        // Act
        await transport.SendAsync(message, redirectTo: null, Ct);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal((LogLevel.Error, 1351), (record.Level, record.Id.Id));
        _ = inner.Received(1).SendAsync(message, null, Ct);
    }

    [Fact]
    public async Task SendAsync_Success_LogsADebugRecordWithItsEventId()
    {
        // Arrange
        var inner = TestTransports.For(TestChannel.Id);
        await using var provider = BuildWith(inner);
        var transport = provider.GetRequiredService<ITransport>();
        var message = TestMessages.AnOutgoingMessage();

        // Act
        await transport.SendAsync(message, redirectTo: null, Ct);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal((LogLevel.Debug, 1350), (record.Level, record.Id.Id));
        _ = inner.Received(1).SendAsync(message, null, Ct);
    }

    [Fact]
    public async Task SendAsync_MessageWithATenant_LogsTheTenantOfTheMessage()
    {
        // Arrange
        var inner = TestTransports.For(TestChannel.Id);
        await using var provider = BuildWith(inner);
        var transport = provider.GetRequiredService<ITransport>();
        var message = TestMessages.AnOutgoingMessage(tenant: new TenantId("t1"));

        // Act
        await transport.SendAsync(message, redirectTo: null, Ct);

        // Assert
        Assert.Equal("t1", Assert.Single(provider.LogRecords()).GetStructuredStateValue("Tenant"));
        _ = inner.Received(1).SendAsync(message, null, Ct);
    }

    [Fact]
    public async Task SendAsync_TestMessage_LogsTheTestFlag()
    {
        // Arrange
        var inner = TestTransports.For(TestChannel.Id);
        await using var provider = BuildWith(inner);
        var transport = provider.GetRequiredService<ITransport>();
        var message = TestMessages.AnOutgoingMessage(flags: MessageFlags.Test);

        // Act
        await transport.SendAsync(message, redirectTo: null, Ct);

        // Assert
        Assert.Equal("Test", Assert.Single(provider.LogRecords()).GetStructuredStateValue("MessageFlags"));
        _ = inner.Received(1).SendAsync(message, null, Ct);
    }

    [Fact]
    public async Task SendAsync_RedirectedTestMessage_NoLogRecordContainsAChannelAddress()
    {
        // Arrange
        var inner = TestTransports.For(TestChannel.Id);
        await using var provider = BuildWith(inner);
        var transport = provider.GetRequiredService<ITransport>();
        var message = TestMessages.AnOutgoingMessage(new TestEnvelope(_address), flags: MessageFlags.Test);

        // Act
        await transport.SendAsync(message, _redirectAddress, Ct);

        // Assert
        Assert.NotEmpty(provider.LogRecords());
        Assert.DoesNotContain(
            provider.LogRecords(), static record => record.ContainsText(_address) || record.ContainsText(_redirectAddress));
        _ = inner.Received(1).SendAsync(message, _redirectAddress, Ct);
    }

    [Fact]
    public async Task CheckHealthAsync_InnerError_ReturnsTheSameError()
    {
        // Arrange
        var expected = new Error(Errors.Unavailable);
        var inner = AHealthCheck(expected);
        await using var provider = BuildWith(inner);
        var transport = provider.GetRequiredService<ITransport>();

        // Act
        var error = await transport.CheckHealthAsync(Ct);

        // Assert
        Assert.Same(expected, error);
        _ = inner.Received(1).CheckHealthAsync(Ct);
    }

    [Fact]
    public async Task CheckHealthAsync_TransientError_LogsAWarningWithItsEventId()
    {
        // Arrange
        var inner = AHealthCheck(new Error(Errors.Unavailable));
        await using var provider = BuildWith(inner);
        var transport = provider.GetRequiredService<ITransport>();

        // Act
        await transport.CheckHealthAsync(Ct);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal((LogLevel.Warning, 1353), (record.Level, record.Id.Id));
        _ = inner.Received(1).CheckHealthAsync(Ct);
    }

    [Fact]
    public async Task CheckHealthAsync_Success_LogsADebugRecordWithItsEventId()
    {
        // Arrange
        var inner = AHealthCheck(error: null);
        await using var provider = BuildWith(inner);
        var transport = provider.GetRequiredService<ITransport>();

        // Act
        var error = await transport.CheckHealthAsync(Ct);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal((null, LogLevel.Debug, 1352), (error, record.Level, record.Id.Id));
        _ = inner.Received(1).CheckHealthAsync(Ct);
    }

    [Fact]
    public async Task UseTransport_EnableLoggingFalse_RegistersNoLoggingWrapper()
    {
        // Arrange
        var inner = TestTransports.For(TestChannel.Id);
        await using var provider = BuildWith(inner, enableLogging: false);

        // Act
        var transport = provider.GetRequiredService<ITransport>();

        // Assert
        Assert.Same(inner, transport);
    }

    private static ITransport AHealthCheck(Error? error)
    {
        var transport = TestTransports.For(TestChannel.Id);
        transport.CheckHealthAsync(default).ReturnsForAnyArgs(ValueTask.FromResult(error));
        return transport;
    }

    private static ServiceProvider BuildWith(ITransport inner, bool enableLogging = true)
    {
        var services = TestHost.CreateServices();
        services.AddMsgHatch(o => o.EnableLogging = enableLogging).UseTransport(_ => inner);
        return services.Build();
    }
}
