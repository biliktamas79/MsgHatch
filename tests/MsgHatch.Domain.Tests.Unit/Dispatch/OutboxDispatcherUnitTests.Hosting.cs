using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Outbox;
using MsgHatch.Domain.Pipeline;
using MsgHatch.Domain.Routing;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using NSubstitute;

namespace MsgHatch.Domain.Tests.Unit.Dispatch;

/// <summary>The layouts of the hosts (D101, D102, D105): the roles, and an event consumer and a publisher in two DI containers.</summary>
public sealed partial class OutboxDispatcherUnitTests
{
    public static TheoryData<MsgHatchRoles, bool, bool, bool> RoleRows => new()
    {
        { MsgHatchRoles.Consumer, false, true, true },
        { MsgHatchRoles.Publisher, false, true, false },
        { MsgHatchRoles.Publisher, true, false, true },
        { MsgHatchRoles.Consumer, true, false, false },
    };

    [Theory]
    [MemberData(nameof(RoleRows))]
    public async Task StartupValidation_Role_RequiresARouterForAConsumerAndATransportForAPublisher(
        MsgHatchRoles roles, bool withTransport, bool withRouter, bool expectedToPass)
    {
        // Arrange
        var services = TestHost.CreateServices(_doubles.Time);
        WithTransportWhen(withTransport, services.AddMsgHatch(o => o.Roles = roles).AddTestChannel().UseInMemoryStores())
            .AddMessage("TestEvent", 2, TestJsonContext.Default.TestEvent, m => m
                .WithRouter((_, _) => withRouter ? PipelineDoubles.RouterGiving<TestEvent>(static () => []) : null!)
                .AsChannel(TestChannel.Id, "TestEnvelope", 1, TestJsonContext.Default.TestEnvelope, TestChannel.AnyComposer<TestEvent>()));
        await using var provider = services.Build();

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        Assert.Equal(expectedToPass, exception is null);
        Assert.True(exception is null or OptionsValidationException);
    }

    [Fact]
    public async Task RunAsync_ConsumerAndPublisherInTwoContainersWithOneSharedOutbox_PublisherSendsTheEventOfTheConsumerAfterAPoll()
    {
        // Arrange
        var outbox = new InMemoryOutboxStore();
        var sent = new TaskCompletionSource<OutgoingMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var transport = TestTransports.For(TestChannel.Id);
        transport.SendAsync(default!, default, Ct).ReturnsForAnyArgs(call =>
        {
            sent.TrySetResult(call.ArgAt<OutgoingMessage>(0));
            return ValueTask.FromResult<ErrorOr<PublishReceipt>>(new PublishReceipt("provider-1"));
        });
        await using var consumer = BuildProcess(outbox, MsgHatchRoles.Consumer);
        await using var publisher = BuildProcess(
            outbox, MsgHatchRoles.Publisher, b => b.UseTransport(_ => transport).ConfigureOutboxDispatch(static o => o.MaxDegreeOfParallelism = 1));
        consumer.GetRequiredService<IStartupValidator>().Validate();
        publisher.GetRequiredService<IStartupValidator>().Validate();
        await using var running = new RunningLoop(publisher.GetRequiredService<IOutboxDispatcher>().RunAsync);
        var poll = await _doubles.Time.NextWaitAsync();
        await using (var scope = consumer.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<INotificationService>()
                .ProcessAsync(new TestEvent("Order 1001"), new MessageContext(Guid.NewGuid(), new TenantId("t1")), Ct);
        }

        // Act
        _doubles.Time.Advance(poll);

        // Assert
        var message = await sent.Task.WaitAsync(TestHost.HangGuard, TimeProvider.System, Ct);
        Assert.Equal(
            ((TenantId?)new TenantId("t1"), new DeliveryTarget(DeliveryTargetKind.User, "u1"), (object)new TestEnvelope("Order 1001 for client1@example.com")),
            (message.Tenant, message.Target, message.Content));
    }

    private ServiceProvider BuildProcess(InMemoryOutboxStore outbox, MsgHatchRoles roles, Action<MsgHatchBuilder>? more = null)
    {
        var services = TestHost.CreateServices(_doubles.Time);
        var builder = services.AddMsgHatch(o => o.Roles = roles)
            .UseOutboxStore((_, key) => outbox.For(key))
            .UseOutboxDispatchStore(_ => outbox);
        RegisterTheMessageTypes(builder);
        more?.Invoke(builder);
        return services.Build();
    }

    private static MsgHatchBuilder WithTransportWhen(bool withTransport, MsgHatchBuilder builder) =>
        withTransport ? builder.UseTransport(static _ => TestTransports.For(TestChannel.Id)) : builder;

    private static void RegisterTheMessageTypes(MsgHatchBuilder builder) =>
        builder.AddTestChannel()
            .AddMessage("TestEvent", 2, TestJsonContext.Default.TestEvent, static m => m
                .WithRouter<TwoRouteTestRouter>()
                .AsChannel<TestEnvelope, TestEnvelopeComposer>(TestChannel.Id, "TestEnvelope", 1, TestJsonContext.Default.TestEnvelope));
}
