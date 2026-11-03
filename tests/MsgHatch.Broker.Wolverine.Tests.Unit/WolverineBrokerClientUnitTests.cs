using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Broker.DependencyInjection;
using MsgHatch.Broker.Tests.Unit.Contracts;
using MsgHatch.Broker.Tests.Unit.TestTypes;
using MsgHatch.Broker.Wolverine.Tests.Unit.TestTypes;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Outbox;
using MsgHatch.Domain.Persistence;
using MsgHatch.Domain.Telemetry;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using NSubstitute;
using Wolverine;
using Wolverine.Runtime.Routing;
using Wolverine.Transports;
using Wolverine.Transports.Sending;

namespace MsgHatch.Broker.Wolverine.Tests.Unit;

/// <summary>
/// The Wolverine broker client: the use cases of the broker client contract with an <see cref="IMessageBus"/>
/// substitute, and the rules that only this client has: the exceptions of Wolverine, the health check, and the publish
/// inside the <c>send</c> span, which Wolverine writes into the header <c>parent-id</c> (section 4.21). The tests with
/// <see cref="WolverineTestHost"/> run a real Wolverine runtime with a fake transport in memory: they check the facts
/// of Wolverine that the client depends on, and the result of each send mode when the broker is not available.
/// </summary>
[Collection(SequentialTelemetry.Name)]
public sealed class WolverineBrokerClientUnitTests : BrokerClientTestsBase<WolverineBrokerClient>
{
    private readonly IMessageBus _bus;

    public WolverineBrokerClientUnitTests()
        : this(new WolverineBrokerClientTestDriver())
    {
    }

    private WolverineBrokerClientUnitTests(WolverineBrokerClientTestDriver driver)
        : base(new WolverineBrokerClient(() => driver.Bus), driver) => _bus = driver.Bus;

    public static TheoryData<string, Exception, int> ExceptionRows => new()
    {
        { "JsonException of the serializer", new JsonException(), 403 },
        { "NotSupportedException of the serializer", new NotSupportedException(), 403 },
        { "WolverineSerializationException of the envelope", new WolverineSerializationException("serializer"), 403 },
        { "SendRejectedException of a transport that refuses the message", new SendRejectedException("refused"), 403 },
        { "MessageTooLargeException of a transport", new MessageTooLargeException(new Envelope(), 1024), 403 },
        { "WolverineHasNotStartedException", new WolverineHasNotStartedException(), 503 },
        { "IOException of a transport", new IOException(), 503 },
        { "TaskCanceledException of a timeout, with no cancelled token", new TaskCanceledException(), 503 },
    };

    public static TheoryData<string, bool, bool, int, int?> SendModeRows => new()
    {
        { "Inline with the sync retry block, the broker never accepts: an error", true, true, int.MaxValue, 503 },
        { "Inline with the sync retry block, the broker accepts the third try: a receipt", true, true, 2, null },
        { "Inline with the default retry block, the broker never accepts: a receipt for a lost message", true, false, int.MaxValue, null },
        { "Buffered, the broker never accepts: a receipt for a lost message", false, true, int.MaxValue, null },
    };

    [Theory]
    [MemberData(nameof(ExceptionRows))]
    public async Task PublishAsync_ExceptionOfWolverine_ReturnsTheErrorCodeOfTheRowWithTheException(string row, Exception exception, int expectedCode)
    {
        // Arrange
        _bus.SendAsync<object>(default!, default).ReturnsForAnyArgs(ValueTask.FromException(exception));

        // Act
        var result = await Client.PublishAsync(APublish(), Ct);

        // Assert
        Assert.Equal((row, expectedCode, exception), (row, result.Error?.Type.Code, result.Error?.Exception));
    }

    [Fact]
    public async Task PublishAsync_PayloadTypeWithNoWolverineRoute_ReturnsNotFound()
    {
        // Arrange
        _bus.SendAsync<object>(default!, default).ReturnsForAnyArgs(_ => throw new IndeterminateRoutesException(typeof(TestPayload)));

        // Act
        var result = await Client.PublishAsync(APublish(), Ct);

        // Assert
        Assert.Equal(Errors.NotFound, result.Error?.Type);
    }

    [Fact]
    public async Task PublishAsync_CancelledToken_PublishesNothing()
    {
        // Arrange
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        await stop.CancelAsync();

        // Act
        await Client.PublishAsync(APublish(), stop.Token);

        // Assert
        Assert.Empty(_bus.ReceivedCalls());
    }

    [Fact]
    public async Task PublishAsync_TokenCancelledWhileWolverinePublishes_ReturnsOperationCancelled()
    {
        // Arrange
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        _bus.SendAsync<object>(default!, default).ReturnsForAnyArgs(_ =>
        {
            stop.Cancel();
            return new ValueTask(new TaskCompletionSource().Task);
        });

        // Act
        var result = await Client.PublishAsync(APublish(), stop.Token).AsTask().WaitAsync(TestHost.HangGuard, TimeProvider.System, Ct);

        // Assert
        Assert.Equal(Errors.OperationCancelled, result.Error?.Type);
    }

    [Fact]
    public async Task CheckHealthAsync_Wolverine_ReturnsNull()
    {
        // Act
        var error = await Client.CheckHealthAsync(Ct);

        // Assert
        Assert.Null(error);
    }

    [Fact]
    public async Task PublishAsync_DispatcherWithAListenerOnTheSource_CallsTheMessageBusWhileTheSendSpanIsCurrent()
    {
        // Arrange
        string? currentAtPublish = null;
        _bus.SendAsync<object>(default!, default).ReturnsForAnyArgs(_ =>
        {
            currentAtPublish = Activity.Current?.Id;
            return ValueTask.CompletedTask;
        });
        var services = TestHost.CreateServices();
        services.AddMsgHatch(static o => o.Roles = MsgHatchRoles.Publisher).UseInMemoryStores().UseBrokerClient(_ => Client);
        await using var provider = services.Build();
        await provider.GetRequiredService<IOutboxStore>().AddAsync([BrokerMessages.AnOutgoingMessage()], Ct);
        var spans = new ConcurrentQueue<Activity>();
        using var listener = SequentialTelemetry.ListenTo([MsgHatchTelemetry.SourceName], spans);

        // Act
        await provider.GetRequiredService<IOutboxDispatcher>().DispatchPendingAsync(10, Ct);

        // Assert
        var send = Assert.Single(spans, static span => span.DisplayName == "send Broker");
        Assert.Equal(send.Id, currentAtPublish);
    }

    [Fact]
    public async Task PublishAsync_RealWolverineWithARouteForTheRunTimeType_SendsThePayloadThroughThatRoute()
    {
        // Arrange
        await using var wolverine = await WolverineTestHost.StartAsync(Ct);
        var payload = new TestPayload("text");

        // Act
        await wolverine.Client.PublishAsync(APublish(payload: payload), Ct);

        // Assert
        Assert.Same(payload, Assert.Single(wolverine.Transport.Sent).Message);
    }

    [Fact]
    public async Task PublishAsync_RealWolverine_SendsTheHeadersAndTheTenantIdInTheEnvelope()
    {
        // Arrange
        await using var wolverine = await WolverineTestHost.StartAsync(Ct);
        var headers = new Dictionary<string, string> { ["msghatch-id"] = "id-1", ["x-source"] = "orders" };

        // Act
        await wolverine.Client.PublishAsync(APublish(tenant: new TenantId("t1"), headers: headers), Ct);

        // Assert
        var sent = Assert.Single(wolverine.Transport.Sent);
        Assert.Equal(("id-1", "orders", "t1"), (sent.Headers["msghatch-id"], sent.Headers["x-source"], sent.TenantId));
    }

    [Fact]
    public async Task PublishAsync_RealWolverineWithACurrentActivity_SendsItsIdAsTheParentId()
    {
        // Arrange
        await using var wolverine = await WolverineTestHost.StartAsync(Ct);
        using var send = new Activity("send Broker").Start();

        // Act
        await wolverine.Client.PublishAsync(APublish(), Ct);

        // Assert
        Assert.Equal(send.Id, Assert.Single(wolverine.Transport.Sent).ParentId);
    }

    [Fact]
    public async Task PublishAsync_RealWolverineWithNoRouteForThePayloadType_ReturnsNotFound()
    {
        // Arrange
        await using var wolverine = await WolverineTestHost.StartAsync(Ct);

        // Act
        var result = await wolverine.Client.PublishAsync(APublish(payload: new OtherPayload(1)), Ct);

        // Assert
        Assert.Equal(Errors.NotFound, result.Error?.Type);
    }

    [Fact]
    public async Task PublishAsync_RealWolverineTransportRefusesTheMessage_ReturnsRejected()
    {
        // Arrange
        await using var wolverine = await WolverineTestHost.StartAsync(static () => new SendRejectedException("refused"), Ct);

        // Act
        var result = await wolverine.Client.PublishAsync(APublish(), Ct);

        // Assert
        Assert.Equal(Errors.Rejected, result.Error?.Type);
    }

    [Fact]
    public async Task PublishAsync_RealWolverineTransportThrowsNotSupportedException_ReturnsAReceiptForALostMessage()
    {
        // Arrange
        await using var wolverine = await WolverineTestHost.StartAsync(static () => new NotSupportedException("transport"), Ct);

        // Act
        var result = await wolverine.Client.PublishAsync(APublish(), Ct);

        // Assert
        Assert.Equal((null, 0), (result.Error, wolverine.Transport.Sent.Count));
    }

    [Theory]
    [MemberData(nameof(SendModeRows))]
    public async Task PublishAsync_SendModeOfRealWolverine_GivesTheResultOfTheRow(
        string row, bool inline, bool syncRetryBlock, int failedSends, int? expectedCode)
    {
        // Arrange
        await using var wolverine = await WolverineTestHost.StartAsync(inline, syncRetryBlock, failedSends, Ct);

        // Act
        var result = await wolverine.Client.PublishAsync(APublish(), Ct);

        // Assert
        Assert.Equal((row, expectedCode), (row, result.Error?.Type.Code));
    }
}
