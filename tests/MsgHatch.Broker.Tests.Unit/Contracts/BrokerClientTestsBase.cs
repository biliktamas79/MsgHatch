using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Tests.Unit.TestTypes;

namespace MsgHatch.Broker.Tests.Unit.Contracts;

/// <summary>
/// The use cases of <see cref="IBrokerClient"/> (section 4.6, D134). Each broker client gets them with one subclass that
/// builds the client and its driver.
/// </summary>
/// <typeparam name="TClient">The broker client.</typeparam>
public abstract class BrokerClientTestsBase<TClient>(TClient client, IBrokerClientTestDriver driver)
    where TClient : class, IBrokerClient
{
    protected TClient Client { get; } = client;

    protected IBrokerClientTestDriver Driver { get; } = driver;

    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task PublishAsync_LibraryAcceptsTheMessage_ReturnsTheReceiptOfTheClient()
    {
        // Arrange
        var receipt = Driver.Accepts();

        // Act
        var result = await Client.PublishAsync(APublish(), Ct);

        // Assert
        Assert.Equal(receipt, result.Value);
    }

    [Fact]
    public async Task PublishAsync_Payload_GivesThePayloadWithItsRunTimeTypeToTheLibrary()
    {
        // Arrange
        Driver.Accepts();
        var payload = new TestPayload("text");

        // Act
        await Client.PublishAsync(APublish(payload: payload), Ct);

        // Assert
        Assert.Same(payload, Driver.Published().Payload);
    }

    [Fact]
    public async Task PublishAsync_Headers_GivesEachHeaderToTheLibrary()
    {
        // Arrange
        Driver.Accepts();
        var headers = new Dictionary<string, string> { ["msghatch-id"] = "id-1", ["x-source"] = "orders" };

        // Act
        await Client.PublishAsync(APublish(headers: headers), Ct);

        // Assert
        Assert.Equal(
            [new KeyValuePair<string, string?>("msghatch-id", "id-1"), new KeyValuePair<string, string?>("x-source", "orders")],
            Driver.Published().Headers.OrderBy(static header => header.Key, StringComparer.Ordinal));
    }

    [Theory]
    [MemberData(nameof(BrokerClientTestRows.TenantRows), MemberType = typeof(BrokerClientTestRows))]
    public async Task PublishAsync_Tenant_GivesTheTenantIdToTheLibrary(string row, string? tenant)
    {
        // Arrange
        Driver.Accepts();

        // Act
        await Client.PublishAsync(APublish(tenant: tenant is null ? null : new TenantId(tenant)), Ct);

        // Assert
        Assert.Equal((row, tenant), (row, Driver.Published().Tenant));
    }

    [Fact]
    public async Task PublishAsync_LibraryGivesATransientError_ReturnsTheErrorAsAValue()
    {
        // Arrange
        Driver.Fails(Errors.Unavailable);

        // Act
        var result = await Client.PublishAsync(APublish(), Ct);

        // Assert
        Assert.Equal(Errors.Unavailable, result.Error?.Type);
    }

    [Fact]
    public async Task PublishAsync_LibraryGivesAPermanentError_ReturnsTheErrorAsAValue()
    {
        // Arrange
        Driver.Fails(Errors.Rejected);

        // Act
        var result = await Client.PublishAsync(APublish(), Ct);

        // Assert
        Assert.Equal(Errors.Rejected, result.Error?.Type);
    }

    [Fact]
    public async Task PublishAsync_CancelledToken_ReturnsOperationCancelled()
    {
        // Arrange
        Driver.WaitsUntilCancelled();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        await stop.CancelAsync();

        // Act
        var result = await Client.PublishAsync(APublish(), stop.Token);

        // Assert
        Assert.Equal(Errors.OperationCancelled, result.Error?.Type);
    }

    protected static BrokerPublish APublish(object? payload = null, TenantId? tenant = null, IReadOnlyDictionary<string, string>? headers = null) =>
        new(Guid.NewGuid(), tenant, payload ?? new TestPayload("text"), headers ?? new Dictionary<string, string> { ["msghatch-id"] = "id-1" });
}
