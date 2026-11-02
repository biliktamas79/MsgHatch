using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Email.Tests.Unit.TestTypes;

namespace MsgHatch.Email.Tests.Unit.Contracts;

/// <summary>
/// The use cases of <see cref="IEmailClient"/> (section 4.6). Each email client gets them with one subclass that builds
/// the client and its driver.
/// </summary>
/// <typeparam name="TClient">The email client.</typeparam>
public abstract class EmailClientTestsBase<TClient>(TClient client, IEmailClientTestDriver driver)
    where TClient : class, IEmailClient
{
    protected TClient Client { get; } = client;

    protected IEmailClientTestDriver Driver { get; } = driver;

    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SendAsync_ProviderAcceptsTheEmail_ReturnsTheMessageIdOfTheProviderInTheReceipt()
    {
        // Arrange
        Driver.Accepts("provider-7");

        // Act
        var result = await Client.SendAsync(Guid.NewGuid(), TestMessages.AnEmail(), Ct);

        // Assert
        Assert.Equal(new PublishReceipt("provider-7"), result.Value);
    }

    [Fact]
    public async Task SendAsync_ProviderGivesATransientError_ReturnsTheErrorAsAValue()
    {
        // Arrange
        Driver.Fails(Errors.Unavailable);

        // Act
        var result = await Client.SendAsync(Guid.NewGuid(), TestMessages.AnEmail(), Ct);

        // Assert
        Assert.Equal(Errors.Unavailable, result.Error?.Type);
    }

    [Fact]
    public async Task SendAsync_ProviderGivesAPermanentError_ReturnsTheErrorAsAValue()
    {
        // Arrange
        Driver.Fails(Errors.Rejected);

        // Act
        var result = await Client.SendAsync(Guid.NewGuid(), TestMessages.AnEmail(), Ct);

        // Assert
        Assert.Equal(Errors.Rejected, result.Error?.Type);
    }

    [Fact]
    public async Task SendAsync_CancelledToken_ReturnsOperationCancelled()
    {
        // Arrange
        Driver.WaitsUntilCancelled();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        await stop.CancelAsync();

        // Act
        var result = await Client.SendAsync(Guid.NewGuid(), TestMessages.AnEmail(), stop.Token);

        // Assert
        Assert.Equal(Errors.OperationCancelled, result.Error?.Type);
    }

    [Fact]
    public async Task CheckHealthAsync_ProviderCanSend_ReturnsNull()
    {
        // Arrange
        Driver.CanSend();

        // Act
        var error = await Client.CheckHealthAsync(Ct);

        // Assert
        Assert.Null(error);
    }

    [Fact]
    public async Task CheckHealthAsync_ProviderCannotSend_ReturnsUnavailable()
    {
        // Arrange
        Driver.CannotSend();

        // Act
        var error = await Client.CheckHealthAsync(Ct);

        // Assert
        Assert.Equal(Errors.Unavailable, error?.Type);
    }
}
