using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MsgHatch.Broker.DependencyInjection;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using NSubstitute;

namespace MsgHatch.Broker.Tests.Unit;

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
        await using var provider = BuildWith(AClient(receipt));

        // Act
        var result = await provider.GetRequiredService<IBrokerClient>().PublishAsync(APublish(), Ct);

        // Assert
        Assert.Equal(receipt, result.Value);
    }

    [Fact]
    public async Task PublishAsync_InnerError_ReturnsTheSameError()
    {
        // Arrange
        var expected = new Error(Errors.Rejected);
        await using var provider = BuildWith(AClient(expected));

        // Act
        var result = await provider.GetRequiredService<IBrokerClient>().PublishAsync(APublish(), Ct);

        // Assert
        Assert.Same(expected, result.Error);
    }

    [Fact]
    public async Task PublishAsync_TransientError_LogsAWarningWithItsEventId()
    {
        // Arrange
        await using var provider = BuildWith(AClient(new Error(Errors.Unavailable)));

        // Act
        await provider.GetRequiredService<IBrokerClient>().PublishAsync(APublish(), Ct);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal((LogLevel.Warning, 2301), (record.Level, record.Id.Id));
    }

    [Fact]
    public async Task PublishAsync_PermanentError_LogsAnErrorWithItsEventId()
    {
        // Arrange
        await using var provider = BuildWith(AClient(new Error(Errors.Rejected)));

        // Act
        await provider.GetRequiredService<IBrokerClient>().PublishAsync(APublish(), Ct);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal((LogLevel.Error, 2301), (record.Level, record.Id.Id));
    }

    [Fact]
    public async Task PublishAsync_CancelledError_LogsInformationWithItsEventId()
    {
        // Arrange
        await using var provider = BuildWith(AClient(new Error(Errors.OperationCancelled)));

        // Act
        await provider.GetRequiredService<IBrokerClient>().PublishAsync(APublish(), Ct);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal((LogLevel.Information, 2301), (record.Level, record.Id.Id));
    }

    [Fact]
    public async Task PublishAsync_Success_LogsADebugRecordWithTheOutgoingMessageId()
    {
        // Arrange
        var publish = APublish();
        await using var provider = BuildWith(AClient(new PublishReceipt("broker-1")));

        // Act
        await provider.GetRequiredService<IBrokerClient>().PublishAsync(publish, Ct);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal(
            (LogLevel.Debug, 2300, publish.MessageId.ToString()),
            (record.Level, record.Id.Id, record.GetStructuredStateValue("OutgoingMessageId")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PublishAsync_PayloadAndHeaderValues_NoLogRecordContainsThem(bool rejected)
    {
        // Arrange
        ErrorOr<PublishReceipt> result = rejected ? new Error(Errors.Rejected) : new PublishReceipt("broker-1");
        await using var provider = BuildWith(AClient(result));

        // Act
        await provider.GetRequiredService<IBrokerClient>().PublishAsync(APublish(), Ct);

        // Assert
        Assert.NotEmpty(provider.LogRecords());
        Assert.DoesNotContain(provider.LogRecords(), static record => record.ContainsText(_payloadText) || record.ContainsText(_headerValue));
    }

    [Fact]
    public async Task CheckHealthAsync_InnerError_ReturnsTheSameError()
    {
        // Arrange
        var expected = new Error(Errors.Unavailable);
        await using var provider = BuildWith(AHealthCheck(expected));

        // Act
        var error = await provider.GetRequiredService<IBrokerClient>().CheckHealthAsync(Ct);

        // Assert
        Assert.Same(expected, error);
    }

    [Fact]
    public async Task CheckHealthAsync_TransientError_LogsAWarningWithItsEventId()
    {
        // Arrange
        await using var provider = BuildWith(AHealthCheck(new Error(Errors.Unavailable)));

        // Act
        await provider.GetRequiredService<IBrokerClient>().CheckHealthAsync(Ct);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal((LogLevel.Warning, 2303), (record.Level, record.Id.Id));
    }

    [Fact]
    public async Task CheckHealthAsync_Success_LogsADebugRecordWithItsEventId()
    {
        // Arrange
        await using var provider = BuildWith(AHealthCheck(error: null));

        // Act
        var error = await provider.GetRequiredService<IBrokerClient>().CheckHealthAsync(Ct);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal((null, LogLevel.Debug, 2302), (error, record.Level, record.Id.Id));
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
        new(Guid.NewGuid(), Tenant: null, new TestPayload(_payloadText), new Dictionary<string, string> { ["x-copy-to"] = _headerValue });

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
