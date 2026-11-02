using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;

namespace MsgHatch.Domain.Tests.Unit.Contracts;

/// <summary>
/// The use cases of <see cref="ITransport"/> (section 4.6, D129). Each transport gets them with one subclass that builds
/// the transport and its driver.
/// </summary>
/// <typeparam name="TTransport">The transport.</typeparam>
public abstract class TransportTestsBase<TTransport>(TTransport transport, ITransportTestDriver driver)
    where TTransport : class, ITransport
{
    protected TTransport Transport { get; } = transport;

    protected ITransportTestDriver Driver { get; } = driver;

    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SendAsync_ProviderAcceptsTheMessage_ReturnsTheReceiptOfTheProvider()
    {
        // Arrange
        var receipt = Driver.Succeeds();

        // Act
        var result = await Transport.SendAsync(Driver.AMessage(), redirectTo: null, Ct);

        // Assert
        Assert.Equal(receipt, result.Value);
    }

    [Fact]
    public async Task SendAsync_ProviderGivesATransientError_ReturnsTheErrorAsAValue()
    {
        // Arrange
        Driver.Fails(Errors.Unavailable);

        // Act
        var result = await Transport.SendAsync(Driver.AMessage(), redirectTo: null, Ct);

        // Assert
        Assert.Equal(Errors.Unavailable, result.Error?.Type);
    }

    [Fact]
    public async Task SendAsync_ProviderGivesAPermanentError_ReturnsTheErrorAsAValue()
    {
        // Arrange
        Driver.Fails(Errors.Rejected);

        // Act
        var result = await Transport.SendAsync(Driver.AMessage(), redirectTo: null, Ct);

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
        var result = await Transport.SendAsync(Driver.AMessage(), redirectTo: null, stop.Token);

        // Assert
        Assert.Equal(Errors.OperationCancelled, result.Error?.Type);
    }
}
