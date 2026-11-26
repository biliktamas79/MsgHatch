using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MsgHatch.Channels.Broker.DependencyInjection;
using MsgHatch.Channels.Broker.Tests.Unit.TestTypes;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using NSubstitute;

namespace MsgHatch.Channels.Broker.Tests.Unit;

/// <summary>
/// The standard logging wrapper tests for <see cref="IBrokerClient"/>. A broker client is not a tenant service, so the
/// wrapper logs the outgoing message id, and never the payload or a header value.
/// </summary>
public sealed class BrokerClientLoggingWrapperUnitTests
{
    private const string _payloadText = "Order 1001 for client1@example.com";
    private const string _headerValue = "client2@example.com";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task PublishAsync_InnerReceipt_ReturnsTheReceiptUnchanged()
    {
        // Arrange
        var receipt = new PublishReceipt("broker-7");
        var (inner, publish) = (AClient(receipt), APublish());
        await using var provider = BuildWith(inner);

        // Act
        var result = await provider.GetRequiredService<IBrokerClient>().PublishAsync(publish, Ct);

        // Assert
        Assert.Equal(receipt, result.Value);
        await inner.Received(1).PublishAsync(publish, Ct);
    }

    [Fact]
    public async Task PublishAsync_InnerError_ReturnsTheSameError()
    {
        // Arrange
        var expected = new Error(Errors.Rejected);
        var (inner, publish) = (AClient(expected), APublish());
        await using var provider = BuildWith(inner);

        // Act
        var result = await provider.GetRequiredService<IBrokerClient>().PublishAsync(publish, Ct);

        // Assert
        Assert.Same(expected, result.Error);
        await inner.Received(1).PublishAsync(publish, Ct);
    }

    [Fact]
    public async Task PublishAsync_TransientError_LogsAWarningWithItsEventId()
    {
        // Arrange
        var (inner, publish) = (AClient(new Error(Errors.Unavailable)), APublish());
        await using var provider = BuildWith(inner);

        // Act
        await provider.GetRequiredService<IBrokerClient>().PublishAsync(publish, Ct);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal((LogLevel.Warning, 2301), (record.Level, record.Id.Id));
        await inner.Received(1).PublishAsync(publish, Ct);
    }

    [Fact]
    public async Task PublishAsync_PermanentError_LogsAnErrorWithItsEventId()
    {
        // Arrange
        var (inner, publish) = (AClient(new Error(Errors.Rejected)), APublish());
        await using var provider = BuildWith(inner);

        // Act
        await provider.GetRequiredService<IBrokerClient>().PublishAsync(publish, Ct);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal((LogLevel.Error, 2301), (record.Level, record.Id.Id));
        await inner.Received(1).PublishAsync(publish, Ct);
    }

    [Fact]
    public async Task PublishAsync_CancelledError_LogsInformationWithItsEventId()
    {
        // Arrange
        var (inner, publish) = (AClient(new Error(Errors.OperationCancelled)), APublish());
        await using var provider = BuildWith(inner);

        // Act
        await provider.GetRequiredService<IBrokerClient>().PublishAsync(publish, Ct);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal((LogLevel.Information, 2301), (record.Level, record.Id.Id));
        await inner.Received(1).PublishAsync(publish, Ct);
    }

    [Fact]
    public async Task PublishAsync_Success_LogsADebugRecordWithTheOutgoingMessageId()
    {
        // Arrange
        var (inner, publish) = (AClient(new PublishReceipt("broker-1")), APublish());
        await using var provider = BuildWith(inner);

        // Act
        await provider.GetRequiredService<IBrokerClient>().PublishAsync(publish, Ct);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal(
            (LogLevel.Debug, 2300, publish.MessageId.ToString()),
            (record.Level, record.Id.Id, record.GetStructuredStateValue("OutgoingMessageId")));
        await inner.Received(1).PublishAsync(publish, Ct);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PublishAsync_PayloadAndHeaderValues_NoLogRecordContainsThem(bool rejected)
    {
        // Arrange
        ErrorOr<PublishReceipt> result = rejected ? new Error(Errors.Rejected) : new PublishReceipt("broker-1");
        var (inner, publish) = (AClient(result), APublish());
        await using var provider = BuildWith(inner);

        // Act
        await provider.GetRequiredService<IBrokerClient>().PublishAsync(publish, Ct);

        // Assert
        Assert.NotEmpty(provider.LogRecords());
        Assert.DoesNotContain(provider.LogRecords(), static record => record.ContainsText(_payloadText) || record.ContainsText(_headerValue));
        await inner.Received(1).PublishAsync(publish, Ct);
    }

    [Fact]
    public async Task CheckHealthAsync_InnerError_ReturnsTheSameError()
    {
        // Arrange
        var expected = new Error(Errors.Unavailable);
        var inner = AHealthCheck(expected);
        await using var provider = BuildWith(inner);

        // Act
        var error = await provider.GetRequiredService<IBrokerClient>().CheckHealthAsync(Ct);

        // Assert
        Assert.Same(expected, error);
        await inner.Received(1).CheckHealthAsync(Ct);
    }

    [Fact]
    public async Task CheckHealthAsync_TransientError_LogsAWarningWithItsEventId()
    {
        // Arrange
        var inner = AHealthCheck(new Error(Errors.Unavailable));
        await using var provider = BuildWith(inner);

        // Act
        await provider.GetRequiredService<IBrokerClient>().CheckHealthAsync(Ct);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal((LogLevel.Warning, 2303), (record.Level, record.Id.Id));
        await inner.Received(1).CheckHealthAsync(Ct);
    }

    [Fact]
    public async Task CheckHealthAsync_Success_LogsADebugRecordWithItsEventId()
    {
        // Arrange
        var inner = AHealthCheck(error: null);
        await using var provider = BuildWith(inner);

        // Act
        var error = await provider.GetRequiredService<IBrokerClient>().CheckHealthAsync(Ct);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal((null, LogLevel.Debug, 2302), (error, record.Level, record.Id.Id));
        await inner.Received(1).CheckHealthAsync(Ct);
    }

    [Fact]
    public async Task UseBrokerClient_EnableLoggingFalse_RegistersNoLoggingWrapper()
    {
        // Arrange
        var inner = AClient(new PublishReceipt("broker-1"));
        await using var provider = BuildWith(inner, enableLogging: false);

        // Act
        var client = provider.GetRequiredService<IBrokerClient>();

        // Assert
        Assert.Same(inner, client);
    }

    private static BrokerPublish APublish() =>
        new(
            Guid.NewGuid(), Tenant: null, BrokerMessages.ContentType, new TestPayload(_payloadText), BrokerTestJsonContext.Default.TestPayload,
            new Dictionary<string, string> { ["x-copy-to"] = _headerValue });

    private static IBrokerClient AClient(ErrorOr<PublishReceipt> result)
    {
        var client = Substitute.For<IBrokerClient>();
        client.PublishAsync(default, default).ReturnsForAnyArgs(ValueTask.FromResult(result));
        return client;
    }

    private static IBrokerClient AHealthCheck(Error? error)
    {
        var client = Substitute.For<IBrokerClient>();
        client.CheckHealthAsync(default).ReturnsForAnyArgs(ValueTask.FromResult(error));
        return client;
    }

    private static ServiceProvider BuildWith(IBrokerClient inner, bool enableLogging = true)
    {
        var services = TestHost.CreateServices();
        services.AddMsgHatch(o => o.EnableLogging = enableLogging).UseBrokerClient(_ => inner);
        return services.Build();
    }
}
