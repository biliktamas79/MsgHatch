using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Channels.Email.DependencyInjection;
using MsgHatch.Channels.Email.Tests.Unit.TestTypes;
using NSubstitute;
using MsgHatch.Templates.Tests.Unit.TestTypes;

namespace MsgHatch.Channels.Email.Tests.Unit;

/// <summary>
/// The standard logging wrapper tests for <see cref="IEmailClient"/>. An email client is not a tenant service, and its
/// calls have no tenant, so the wrapper logs the outgoing message id.
/// </summary>
public sealed class EmailClientLoggingWrapperUnitTests
{
    private const string _address = "client1@example.com";
    private const string _subject = "Order 1001 shipped";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SendAsync_InnerReceipt_ReturnsTheReceiptUnchanged()
    {
        // Arrange
        var receipt = new PublishReceipt("ses-7");
        var (inner, id, email) = (AClient(receipt), Guid.NewGuid(), TestMessages.AnEmail());
        await using var provider = BuildWith(inner);

        // Act
        var result = await provider.GetRequiredService<IEmailClient>().SendAsync(id, email, Ct);

        // Assert
        Assert.Equal(receipt, result.Value);
        await inner.Received(1).SendAsync(id, email, Ct);
    }

    [Fact]
    public async Task SendAsync_InnerError_ReturnsTheSameError()
    {
        // Arrange
        var expected = new Error(Errors.Rejected);
        var (inner, id, email) = (AClient(expected), Guid.NewGuid(), TestMessages.AnEmail());
        await using var provider = BuildWith(inner);

        // Act
        var result = await provider.GetRequiredService<IEmailClient>().SendAsync(id, email, Ct);

        // Assert
        Assert.Same(expected, result.Error);
        await inner.Received(1).SendAsync(id, email, Ct);
    }

    [Fact]
    public async Task SendAsync_TransientError_LogsAWarningWithItsEventId()
    {
        // Arrange
        var (inner, id, email) = (AClient(new Error(Errors.RateLimited)), Guid.NewGuid(), TestMessages.AnEmail());
        await using var provider = BuildWith(inner);

        // Act
        await provider.GetRequiredService<IEmailClient>().SendAsync(id, email, Ct);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal((LogLevel.Warning, 2005), (record.Level, record.Id.Id));
        await inner.Received(1).SendAsync(id, email, Ct);
    }

    [Fact]
    public async Task SendAsync_PermanentError_LogsAnErrorWithItsEventId()
    {
        // Arrange
        var (inner, id, email) = (AClient(new Error(Errors.Rejected)), Guid.NewGuid(), TestMessages.AnEmail());
        await using var provider = BuildWith(inner);

        // Act
        await provider.GetRequiredService<IEmailClient>().SendAsync(id, email, Ct);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal((LogLevel.Error, 2005), (record.Level, record.Id.Id));
        await inner.Received(1).SendAsync(id, email, Ct);
    }

    [Fact]
    public async Task SendAsync_CancelledError_LogsInformationWithItsEventId()
    {
        // Arrange
        var (inner, id, email) = (AClient(new Error(Errors.OperationCancelled)), Guid.NewGuid(), TestMessages.AnEmail());
        await using var provider = BuildWith(inner);

        // Act
        await provider.GetRequiredService<IEmailClient>().SendAsync(id, email, Ct);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal((LogLevel.Information, 2005), (record.Level, record.Id.Id));
        await inner.Received(1).SendAsync(id, email, Ct);
    }

    [Fact]
    public async Task SendAsync_Success_LogsADebugRecordWithTheOutgoingMessageId()
    {
        // Arrange
        var (inner, id, email) = (AClient(new PublishReceipt("ses-1")), Guid.NewGuid(), TestMessages.AnEmail());
        await using var provider = BuildWith(inner);

        // Act
        await provider.GetRequiredService<IEmailClient>().SendAsync(id, email, Ct);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal((LogLevel.Debug, 2004, id.ToString()), (record.Level, record.Id.Id, record.GetStructuredStateValue("OutgoingMessageId")));
        await inner.Received(1).SendAsync(id, email, Ct);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SendAsync_EmailWithAnAddressAndASubject_NoLogRecordContainsThem(bool rejected)
    {
        // Arrange
        ErrorOr<PublishReceipt> result = rejected ? new Error(Errors.Rejected) : new PublishReceipt("ses-1");
        var (inner, id) = (AClient(result), Guid.NewGuid());
        await using var provider = BuildWith(inner);
        var email = TestMessages.AnEmail(to: _address, subject: _subject, cc: [_address, _address], bcc: [_address, _address]);

        // Act
        await provider.GetRequiredService<IEmailClient>().SendAsync(id, email, Ct);

        // Assert
        Assert.NotEmpty(provider.LogRecords());
        Assert.DoesNotContain(provider.LogRecords(), static record => record.ContainsText(_address) || record.ContainsText(_subject));
        await inner.Received(1).SendAsync(id, email, Ct);
    }

    [Fact]
    public async Task CheckHealthAsync_InnerError_ReturnsTheSameError()
    {
        // Arrange
        var expected = new Error(Errors.Unavailable);
        var inner = AHealthCheck(expected);
        await using var provider = BuildWith(inner);

        // Act
        var error = await provider.GetRequiredService<IEmailClient>().CheckHealthAsync(Ct);

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
        await provider.GetRequiredService<IEmailClient>().CheckHealthAsync(Ct);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal((LogLevel.Warning, 2007), (record.Level, record.Id.Id));
        await inner.Received(1).CheckHealthAsync(Ct);
    }

    [Fact]
    public async Task CheckHealthAsync_Success_LogsADebugRecordWithItsEventId()
    {
        // Arrange
        var inner = AHealthCheck(error: null);
        await using var provider = BuildWith(inner);

        // Act
        var error = await provider.GetRequiredService<IEmailClient>().CheckHealthAsync(Ct);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal((null, LogLevel.Debug, 2006), (error, record.Level, record.Id.Id));
        await inner.Received(1).CheckHealthAsync(Ct);
    }

    [Fact]
    public async Task UseEmailClient_EnableLoggingFalse_RegistersNoLoggingWrapper()
    {
        // Arrange
        var inner = AClient(new PublishReceipt("ses-1"));
        await using var provider = BuildWith(inner, enableLogging: false);

        // Act
        var client = provider.GetRequiredService<IEmailClient>();

        // Assert
        Assert.Same(inner, client);
    }

    private static IEmailClient AClient(ErrorOr<PublishReceipt> result)
    {
        var client = Substitute.For<IEmailClient>();
        client.SendAsync(default, default!, default).ReturnsForAnyArgs(ValueTask.FromResult(result));
        return client;
    }

    private static IEmailClient AHealthCheck(Error? error)
    {
        var client = Substitute.For<IEmailClient>();
        client.CheckHealthAsync(default).ReturnsForAnyArgs(ValueTask.FromResult(error));
        return client;
    }

    private static ServiceProvider BuildWith(IEmailClient inner, bool enableLogging = true)
    {
        var services = TestHost.CreateServices();
        services.AddMsgHatch(o => o.EnableLogging = enableLogging).UseEmailClient(_ => inner);
        return services.Build();
    }
}
