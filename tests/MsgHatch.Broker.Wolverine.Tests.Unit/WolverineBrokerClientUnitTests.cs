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
using MsgHatch.Domain.Outbox;
using MsgHatch.Domain.Persistence;
using MsgHatch.Domain.Telemetry;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using NSubstitute;
using Wolverine;
using Wolverine.Runtime.Routing;

namespace MsgHatch.Broker.Wolverine.Tests.Unit;

/// <summary>
/// The Wolverine broker client: the use cases of the broker client contract with an <see cref="IMessageBus"/>
/// substitute, and the rules that only this client has: the exceptions of Wolverine, the health check, and the publish
/// inside the <c>send</c> span, which Wolverine writes into the header <c>parent-id</c> (section 4.21).
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
        { "WolverineHasNotStartedException", new WolverineHasNotStartedException(), 503 },
        { "IOException of a transport", new IOException(), 503 },
        { "TaskCanceledException of a timeout, with no cancelled token", new TaskCanceledException(), 503 },
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
}
