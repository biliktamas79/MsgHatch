using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Email.DependencyInjection;
using MsgHatch.Email.Tests.Unit.TestTypes;
using NSubstitute;

namespace MsgHatch.Email.Tests.Unit;

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
        await using var provider = BuildWith(AClient(receipt));

        // Act
        var result = await provider.GetRequiredService<IEmailClient>().SendAsync(Guid.NewGuid(), TestMessages.AnEmail(), Ct);

        // Assert
        Assert.Equal(receipt, result.Value);
    }

    [Fact]
    public async Task SendAsync_InnerError_ReturnsTheSameError()
    {
        // Arrange
        var expected = new Error(Errors.Rejected);
        await using var provider = BuildWith(AClient(expected));

        // Act
        var result = await provider.GetRequiredService<IEmailClient>().SendAsync(Guid.NewGuid(), TestMessages.AnEmail(), Ct);

        // Assert
        Assert.Same(expected, result.Error);
    }

    [Fact]
    public async Task SendAsync_TransientError_LogsAWarningWithItsEventId()
    {
        // Arrange
        await using var provider = BuildWith(AClient(new Error(Errors.RateLimited)));

        // Act
        await provider.GetRequiredService<IEmailClient>().SendAsync(Guid.NewGuid(), TestMessages.AnEmail(), Ct);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal((LogLevel.Warning, 2005), (record.Level, record.Id.Id));
    }

    [Fact]
    public async Task SendAsync_PermanentError_LogsAnErrorWithItsEventId()
    {
        // Arrange
        await using var provider = BuildWith(AClient(new Error(Errors.Rejected)));

        // Act
        await provider.GetRequiredService<IEmailClient>().SendAsync(Guid.NewGuid(), TestMessages.AnEmail(), Ct);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal((LogLevel.Error, 2005), (record.Level, record.Id.Id));
    }

    [Fact]
    public async Task SendAsync_CancelledError_LogsInformationWithItsEventId()
    {
        // Arrange
        await using var provider = BuildWith(AClient(new Error(Errors.OperationCancelled)));

        // Act
        await provider.GetRequiredService<IEmailClient>().SendAsync(Guid.NewGuid(), TestMessages.AnEmail(), Ct);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal((LogLevel.Information, 2005), (record.Level, record.Id.Id));
    }

    [Fact]
    public async Task SendAsync_Success_LogsADebugRecordWithTheOutgoingMessageId()
    {
        // Arrange
        var id = Guid.NewGuid();
        await using var provider = BuildWith(AClient(new PublishReceipt("ses-1")));

        // Act
        await provider.GetRequiredService<IEmailClient>().SendAsync(id, TestMessages.AnEmail(), Ct);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal((LogLevel.Debug, 2004, id.ToString()), (record.Level, record.Id.Id, record.GetStructuredStateValue("OutgoingMessageId")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SendAsync_EmailWithAnAddressAndASubject_NoLogRecordContainsThem(bool rejected)
    {
        // Arrange
        ErrorOr<PublishReceipt> result = rejected ? new Error(Errors.Rejected) : new PublishReceipt("ses-1");
        await using var provider = BuildWith(AClient(result));
        var email = TestMessages.AnEmail(to: _address, subject: _subject, cc: [_address, _address], bcc: [_address, _address]);

        // Act
        await provider.GetRequiredService<IEmailClient>().SendAsync(Guid.NewGuid(), email, Ct);

        // Assert
        Assert.NotEmpty(provider.LogRecords());
        Assert.DoesNotContain(provider.LogRecords(), static record => record.ContainsText(_address) || record.ContainsText(_subject));
    }

    [Fact]
    public async Task CheckHealthAsync_InnerError_ReturnsTheSameError()
    {
        // Arrange
        var expected = new Error(Errors.Unavailable);
        await using var provider = BuildWith(AHealthCheck(expected));

        // Act
        var error = await provider.GetRequiredService<IEmailClient>().CheckHealthAsync(Ct);

        // Assert
        Assert.Same(expected, error);
    }

    [Fact]
    public async Task CheckHealthAsync_TransientError_LogsAWarningWithItsEventId()
    {
        // Arrange
        await using var provider = BuildWith(AHealthCheck(new Error(Errors.Unavailable)));

        // Act
        await provider.GetRequiredService<IEmailClient>().CheckHealthAsync(Ct);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal((LogLevel.Warning, 2007), (record.Level, record.Id.Id));
    }

    [Fact]
    public async Task CheckHealthAsync_Success_LogsADebugRecordWithItsEventId()
    {
        // Arrange
        await using var provider = BuildWith(AHealthCheck(error: null));

        // Act
        var error = await provider.GetRequiredService<IEmailClient>().CheckHealthAsync(Ct);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal((null, LogLevel.Debug, 2006), (error, record.Level, record.Id.Id));
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
