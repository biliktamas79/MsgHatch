using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Pipeline;
using MsgHatch.Domain.Routing;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using static MsgHatch.Domain.Tests.Unit.TestTypes.TestRoutes;

namespace MsgHatch.Domain.Tests.Unit.Pipeline;

public sealed partial class NotificationServiceUnitTests
{
    public static TheoryData<string, int> RouteCheckRows => new()
    {
        { "UnregisteredChannel", 404 },
        { "ChannelWithNoRegistrationOfTheMessageType", 404 },
        { "OtherSettingsType", 400 },
        { "NullSettings", 400 },
        { "SecondRouteForTheSameTargetAndChannel", 409 },
    };

    private static Dictionary<string, MessageRoute> FailingRoutes => new()
    {
        ["UnregisteredChannel"] = ARoute("u9", channel: new ChannelId("Unregistered")),
        ["ChannelWithNoRegistrationOfTheMessageType"] = ARoute("u9", channel: TestChannel.ThirdId),
        ["OtherSettingsType"] = ARoute("u9", settings: new OtherTestChannelSettings()),
        ["NullSettings"] = ARoute("u9") with { Settings = null! },
        ["SecondRouteForTheSameTargetAndChannel"] = ARoute("u1", address: "second@example.com"),
    };

    [Fact]
    public async Task ProcessAsync_Event_CallsTheRouterOfTheMessageTypeOneTimeWithTheReceivedMessage()
    {
        // Arrange
        var domainEvent = new TestEvent("text");
        var context = AContext(_tenant, MessageFlags.Test);
        await using var provider = _doubles.Build(_time);

        // Act
        await ProcessAsync(provider, domainEvent, context);

        // Assert
        _ = _doubles.Router(_t1).Received(1).RouteAsync(
            Arg.Is<ReceivedMessage<TestEvent>>(m =>
                m.Event == domainEvent && m.Delivery.Id == context.MessageId && m.Tenant == context.Tenant
                && m.Flags == context.Flags && m.Type == new MessageType("TestEvent") && m.ContentVersion == 2
                && m.Delivery.CreatedAtUtc == TestHost.Start.UtcDateTime),
            Ct);
    }

    [Fact]
    public async Task ProcessAsync_RoutesOfTwoChannels_CallsEachComposerOneTimeWithAllRoutesOfItsChannelInTheOrderOfTheRouter()
    {
        // Arrange
        MessageRoute[] routes =
        [
            ARoute("u1"),
            ARoute("u2", channel: TestChannel.SecondId, settings: new OtherTestChannelSettings()),
            ARoute("u3"),
            ARoute("u4", channel: TestChannel.SecondId, settings: new OtherTestChannelSettings()),
        ];
        _doubles.Routes = routes;
        var context = AContext();
        await using var provider = _doubles.Build(_time);

        // Act
        await ProcessAsync(provider, context);

        // Assert
        _ = _doubles.Router(TenantKey.None).Received(1).RouteAsync(ReceivedMessageOf(context), Ct);
        _ = _doubles.Composer(TenantKey.None).Received(1).ComposeAsync(
            ReceivedMessageOf(context),
            Arg.Is<ReadOnlyMemory<MessageRoute>>(r => r.ToArray().SequenceEqual(new[] { routes[0], routes[2] })),
            Ct);
        _ = _doubles.SecondComposer(TenantKey.None).Received(1).ComposeAsync(
            ReceivedMessageOf(context),
            Arg.Is<ReadOnlyMemory<MessageRoute>>(r => r.ToArray().SequenceEqual(new[] { routes[1], routes[3] })),
            Ct);
        _ = _doubles.Store(TenantKey.None).Received(1).AddAsync(OutgoingMessagesTo("u1", "u2", "u3", "u4"), Ct);
    }

    [Fact]
    public async Task ProcessAsync_ContextWithATenant_UsesTheRouterComposerAndOutboxStoreOfTheKeyOfTheTenant()
    {
        // Arrange
        _doubles.Routes = [ARoute("u1")];
        var context = AContext(_tenant);
        await using var provider = _doubles.Build(_time);

        // Act
        await ProcessAsync(provider, context);

        // Assert
        _ = _doubles.Router(_t1).Received(1).RouteAsync(ReceivedMessageOf(context), Ct);
        _ = _doubles.Composer(_t1).Received(1).ComposeAsync(ReceivedMessageOf(context), RoutesTo("u1"), Ct);
        _ = _doubles.Store(_t1).Received(1).AddAsync(Arg.Is<IReadOnlyList<OutgoingMessage>>(m => m.Single().Tenant == _tenant), Ct);
        _ = _doubles.Router(TenantKey.None).DidNotReceiveWithAnyArgs().RouteAsync(default!, Ct);
        _ = _doubles.Store(TenantKey.None).DidNotReceiveWithAnyArgs().AddAsync(default!, Ct);
    }

    [Fact]
    public async Task ProcessAsync_TwoTenantsAndNoTenantTwoTimes_UsesTheServicesOfTheKeyOfEachEventAtEachCall()
    {
        // Arrange
        TenantId?[] tenants = [new TenantId("boxes-a"), new TenantId("boxes-b"), null]; // no other test uses these tenants, so the first call makes their boxes
        _doubles.Routes = [ARoute("u1")];
        await using var provider = _doubles.Build(_time);

        // Act
        await ProcessOneAfterTheOtherAsync(provider, tenants.Concat(tenants));

        // Assert
        Assert.Equal(
            [(2, 2, 2), (2, 2, 2), (2, 2, 2)],
            tenants.Select(TenantKey.For).Select(key => (
                _doubles.Router(key).ReceivedCalls().Count(), _doubles.Composer(key).ReceivedCalls().Count(), _doubles.Store(key).ReceivedCalls().Count())));
    }

    [Fact]
    public async Task ProcessAsync_MoreTenantsThanTheLimitOfTheKeyBoxes_UsesTheServicesOfTheKeyOfTheLastTenantAtEachCall()
    {
        // Arrange
        TenantId?[] tenants = [.. Enumerable.Range(0, 1_100).Select(static index => new TenantId($"limit-{index}"))]; // more than the 1,024 boxes that are kept
        _doubles.Routes = [ARoute("u1")];
        await using var provider = _doubles.Build(_time, enableLogging: false);
        await ProcessOneAfterTheOtherAsync(provider, tenants);

        // Act
        await ProcessAsync(provider, AContext(tenants[^1]));

        // Assert
        var last = TenantKey.For(tenants[^1]);
        Assert.Equal(
            (2, 2, 2),
            (_doubles.Router(last).ReceivedCalls().Count(), _doubles.Composer(last).ReceivedCalls().Count(), _doubles.Store(last).ReceivedCalls().Count()));
    }

    [Fact]
    public async Task ProcessAsync_ContextWithNoTenant_UsesTheRouterComposerAndOutboxStoreOfTenantKeyNone()
    {
        // Arrange
        _doubles.Routes = [ARoute("u1")];
        var context = AContext(tenant: null);
        await using var provider = _doubles.Build(_time);

        // Act
        await ProcessAsync(provider, context);

        // Assert
        _ = _doubles.Router(TenantKey.None).Received(1).RouteAsync(ReceivedMessageOf(context), Ct);
        _ = _doubles.Composer(TenantKey.None).Received(1).ComposeAsync(ReceivedMessageOf(context), RoutesTo("u1"), Ct);
        _ = _doubles.Store(TenantKey.None).Received(1).AddAsync(Arg.Is<IReadOnlyList<OutgoingMessage>>(m => m.Single().Tenant == null), Ct);
        _ = _doubles.Router(_t1).DidNotReceiveWithAnyArgs().RouteAsync(default!, Ct);
    }

    [Theory]
    [MemberData(nameof(RouteCheckRows))]
    public async Task ProcessAsync_RouteThatFailsACheck_FailsOnlyThatRouteAndStoresTheOthers(string failingRoute, int errorCode)
    {
        // Arrange
        _doubles.Routes = [ARoute("u1"), FailingRoutes[failingRoute], ARoute("u2")];
        var context = AContext();
        await using var provider = _doubles.Build(_time);

        // Act
        var result = await ProcessAsync(provider, context);

        // Assert
        Assert.Equal((2, 1), (result.Value.Stored, result.Value.Failed));
        Assert.Equal([errorCode], result.Value.Errors.Select(static e => e.Type.Code));
        Assert.Equal(["u1", "u2"], _doubles.Stored.Select(static m => m.Target.Id));
        _ = _doubles.Composer(TenantKey.None).Received(1).ComposeAsync(ReceivedMessageOf(context), RoutesTo("u1", "u2"), Ct);
        _ = _doubles.Store(TenantKey.None).Received(1).AddAsync(OutgoingMessagesTo("u1", "u2"), Ct);
    }

    [Fact]
    public async Task ProcessAsync_EmptyRouteList_StoresNothing()
    {
        // Arrange
        _doubles.Routes = [];
        var context = AContext();
        await using var provider = _doubles.Build(_time);

        // Act
        var result = await ProcessAsync(provider, context);

        // Assert
        Assert.Equal((0, 0), (result.Value.Stored, result.Value.Failed));
        _ = _doubles.Router(TenantKey.None).Received(1).RouteAsync(ReceivedMessageOf(context), Ct);
        _ = _doubles.Composer(TenantKey.None).DidNotReceiveWithAnyArgs().ComposeAsync(default!, default, Ct);
        _ = _doubles.Store(TenantKey.None).DidNotReceiveWithAnyArgs().AddAsync(default!, Ct);
    }

    [Fact]
    public async Task ProcessAsync_Routes_EachOutgoingMessageHasTheTenantTypeTargetAndTraceOfItsRoute()
    {
        // Arrange
        var first = ARoute("u1", trace: new TestTrace("first"));
        var second = ARoute("u2", trace: new TestTrace("second"));
        _doubles.Routes = [first, second];
        var context = AContext(_tenant);
        await using var provider = _doubles.Build(_time);

        // Act
        await ProcessAsync(provider, context);

        // Assert
        Assert.Equal(
            [
                (context.MessageId, (TenantId?)_tenant, new MessageType("TestEvent"), first.Target, first.Trace),
                (context.MessageId, _tenant, new MessageType("TestEvent"), second.Target, second.Trace),
            ],
            _doubles.Stored.Select(static m => (m.SourceMessageId, m.Tenant, m.Type, m.Target, m.Trace)));
        _ = _doubles.Store(_t1).Received(1).AddAsync(OutgoingMessagesTo("u1", "u2"), Ct);
    }

    [Fact]
    public async Task ProcessAsync_Routes_EachOutgoingMessageHasTheChannelContentTypeAndEnvelopeOfItsRegistration()
    {
        // Arrange
        _doubles.Routes = [ARoute("u1", address: "client1@example.com")];
        var context = AContext();
        await using var provider = _doubles.Build(_time);

        // Act
        await ProcessAsync(provider, context);

        // Assert
        var message = Assert.IsType<OutgoingMessage<TestEnvelope>>(Assert.Single(_doubles.Stored));
        Assert.Equal(
            (TestChannel.Id, "Test:TestEnvelope", 1, 1, new TestEnvelope("client1@example.com")),
            (message.Channel, message.ContentType, message.ContentVersion, message.SchemaVersion, message.Envelope));
        _ = _doubles.Composer(TenantKey.None).Received(1).ComposeAsync(ReceivedMessageOf(context), RoutesTo("u1"), Ct);
        _ = _doubles.Store(TenantKey.None).Received(1).AddAsync(OutgoingMessagesTo("u1"), Ct);
    }

    [Fact]
    public async Task ProcessAsync_SameContextTwoTimes_GivesTheSameOutgoingMessageIdsBothTimes()
    {
        // Arrange
        _doubles.Routes = [ARoute("u1"), ARoute("u2")];
        var context = AContext(_tenant);
        await using var provider = _doubles.Build(_time);
        await ProcessAsync(provider, context);
        Guid[] firstIds = [.. _doubles.Stored.Select(static m => m.Delivery.Id)];
        _doubles.Stored.Clear();

        // Act
        await ProcessAsync(provider, context);

        // Assert
        Assert.Equal(firstIds, _doubles.Stored.Select(static m => m.Delivery.Id));
        _ = _doubles.Store(_t1).Received(2).AddAsync(OutgoingMessagesTo("u1", "u2"), Ct);
    }

    [Fact]
    public async Task ProcessAsync_SameMessageIdWithAnotherTenant_GivesOtherOutgoingMessageIds()
    {
        // Arrange
        _doubles.Routes = [ARoute("u1"), ARoute("u2")];
        var context = AContext(_tenant);
        await using var provider = _doubles.Build(_time);
        await ProcessAsync(provider, context);
        Guid[] firstIds = [.. _doubles.Stored.Select(static m => m.Delivery.Id)];
        _doubles.Stored.Clear();

        // Act
        await ProcessAsync(provider, context with { Tenant = new TenantId("t2") });

        // Assert
        Assert.Empty(firstIds.Intersect(_doubles.Stored.Select(static m => m.Delivery.Id)));
        Assert.Equal(2, _doubles.Stored.Count);
        _ = _doubles.Store(_t1).Received(1).AddAsync(OutgoingMessagesTo("u1", "u2"), Ct);
        _ = _doubles.Store(TenantKey.For(new TenantId("t2"))).Received(1).AddAsync(OutgoingMessagesTo("u1", "u2"), Ct);
    }

    [Fact]
    public async Task ProcessAsync_UnregisteredEventType_ReturnsNotFound()
    {
        // Arrange
        await using var provider = _doubles.Build(_time);

        // Act
        var result = await ProcessAsync(provider, new OtherTestEvent("text"), AContext());

        // Assert
        Assert.Equal(Errors.NotFound, result.Error?.Type);
        _ = _doubles.Router(TenantKey.None).DidNotReceiveWithAnyArgs().RouteAsync(default!, Ct);
        _ = _doubles.Store(TenantKey.None).DidNotReceiveWithAnyArgs().AddAsync(default!, Ct);
    }

    [Fact]
    public async Task ProcessAsync_RouterError_FailsTheEventWithThatError()
    {
        // Arrange
        var error = new Error(Errors.Unavailable);
        _doubles.Router(TenantKey.None).RouteAsync(default!, Ct)
            .ReturnsForAnyArgs(ValueTask.FromResult<ErrorOr<IReadOnlyList<MessageRoute>>>(error));
        var context = AContext();
        await using var provider = _doubles.Build(_time);

        // Act
        var result = await ProcessAsync(provider, context);

        // Assert
        Assert.Same(error, result.Error);
        _ = _doubles.Router(TenantKey.None).Received(1).RouteAsync(ReceivedMessageOf(context), Ct);
        _ = _doubles.Composer(TenantKey.None).DidNotReceiveWithAnyArgs().ComposeAsync(default!, default, Ct);
        _ = _doubles.Store(TenantKey.None).DidNotReceiveWithAnyArgs().AddAsync(default!, Ct);
    }

    [Fact]
    public async Task ProcessAsync_AddAsyncError_FailsTheEventWithThatError()
    {
        // Arrange
        var error = new Error(Errors.Unavailable);
        _doubles.Routes = [ARoute("u1")];
        _doubles.Store(TenantKey.None).AddAsync(default!, Ct).ReturnsForAnyArgs(ValueTask.FromResult<Error?>(error));
        await using var provider = _doubles.Build(_time);

        // Act
        var result = await ProcessAsync(provider, AContext());

        // Assert
        Assert.Same(error, result.Error);
        _ = _doubles.Store(TenantKey.None).Received(1).AddAsync(OutgoingMessagesTo("u1"), Ct);
    }

    [Fact]
    public async Task ProcessAsync_InnerComposerErrorForOneRoute_FailsOnlyThatMessage()
    {
        // Arrange
        var error = new Error(Errors.ValidationFailed);
        _doubles.Routes = [ARoute("u1"), ARoute("u2")];
        _doubles.Composer(TenantKey.None).ComposeAsync(default!, default, Ct)
            .ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<IReadOnlyList<ErrorOr<TestEnvelope>>>.ForValue(
                [new TestEnvelope("first"), error])));
        var context = AContext();
        await using var provider = _doubles.Build(_time);

        // Act
        var result = await ProcessAsync(provider, context);

        // Assert
        Assert.Equal((1, 1), (result.Value.Stored, result.Value.Failed));
        Assert.Equal([error], result.Value.Errors);
        Assert.Equal(["u1"], _doubles.Stored.Select(static m => m.Target.Id));
        _ = _doubles.Composer(TenantKey.None).Received(1).ComposeAsync(ReceivedMessageOf(context), RoutesTo("u1", "u2"), Ct);
        _ = _doubles.Store(TenantKey.None).Received(1).AddAsync(OutgoingMessagesTo("u1"), Ct);
    }

    [Fact]
    public async Task ProcessAsync_OuterComposerError_FailsTheEventAndStoresNothing()
    {
        // Arrange
        var error = new Error(Errors.Unavailable);
        _doubles.Routes = [ARoute("u1")];
        _doubles.Composer(TenantKey.None).ComposeAsync(default!, default, Ct)
            .ReturnsForAnyArgs(ValueTask.FromResult<ErrorOr<IReadOnlyList<ErrorOr<TestEnvelope>>>>(error));
        var context = AContext();
        await using var provider = _doubles.Build(_time);

        // Act
        var result = await ProcessAsync(provider, context);

        // Assert
        Assert.Same(error, result.Error);
        Assert.Empty(_doubles.Stored);
        _ = _doubles.Composer(TenantKey.None).Received(1).ComposeAsync(ReceivedMessageOf(context), RoutesTo("u1"), Ct);
        _ = _doubles.Store(TenantKey.None).DidNotReceiveWithAnyArgs().AddAsync(default!, Ct);
    }

    [Fact]
    public async Task ProcessAsync_ComposerWithANullEnvelopeForOneRoute_FailsOnlyThatMessageWithValidationFailed()
    {
        // Arrange
        _doubles.Routes = [ARoute("u1"), ARoute("u2")];
        _doubles.Composer(TenantKey.None).ComposeAsync(default!, default, Ct)
            .ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<IReadOnlyList<ErrorOr<TestEnvelope>>>.ForValue(
                [new TestEnvelope("first"), ErrorOr<TestEnvelope>.ForValue(null!)]))); // no envelope for the second route
        await using var provider = _doubles.Build(_time);

        // Act
        var result = await ProcessAsync(provider, AContext());

        // Assert
        Assert.Equal([Errors.ValidationFailed], result.Value.Errors.Select(static e => e.Type));
        Assert.Equal(["u1"], _doubles.Stored.Select(static m => m.Target.Id));
        _ = _doubles.Store(TenantKey.None).Received(1).AddAsync(OutgoingMessagesTo("u1"), Ct);
    }

    [Fact]
    public async Task ProcessAsync_CancelledTokenThatTheRouterObserves_ReturnsOperationCancelled()
    {
        // Arrange
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        await cancellation.CancelAsync();
        _doubles.Router(TenantKey.None).RouteAsync(default!, Ct).ThrowsForAnyArgs(new OperationCanceledException(cancellation.Token));
        var context = AContext();
        await using var provider = _doubles.Build(_time);

        // Act
        var result = await ProcessAsync(provider, context, cancellation.Token);

        // Assert
        Assert.Equal(Errors.OperationCancelled, result.Error?.Type);
        _ = _doubles.Router(TenantKey.None).Received(1).RouteAsync(ReceivedMessageOf(context), cancellation.Token);
        _ = _doubles.Composer(TenantKey.None).DidNotReceiveWithAnyArgs().ComposeAsync(default!, default, Ct);
    }

    [Fact]
    public async Task ProcessAsync_RouterThatThrowsOperationCanceledExceptionWithNoCancelledToken_ReturnsTheExceptionError()
    {
        // Arrange
        _doubles.Router(TenantKey.None).RouteAsync(default!, Ct).ThrowsForAnyArgs(new OperationCanceledException()); // the token of the call is not cancelled
        var context = AContext();
        await using var provider = _doubles.Build(_time);

        // Act
        var result = await ProcessAsync(provider, context);

        // Assert
        Assert.Equal(Errors.Exception, result.Error?.Type);
        _ = _doubles.Router(TenantKey.None).Received(1).RouteAsync(ReceivedMessageOf(context), Ct);
        _ = _doubles.Composer(TenantKey.None).DidNotReceiveWithAnyArgs().ComposeAsync(default!, default, Ct);
        _ = _doubles.Store(TenantKey.None).DidNotReceiveWithAnyArgs().AddAsync(default!, Ct);
    }

    [Fact]
    public async Task ProcessAsync_MessageTypeWithNoRouter_ReturnsNotFound()
    {
        // Arrange
        var services = TestHost.CreateServices(_time);
        services.AddMsgHatch().AddTestChannel().UseInMemoryStores()
            .AddMessage("TestEvent", 2, TestJsonContext.Default.TestEvent, static m => m // no router
                .AsChannel(TestChannel.Id, "TestEnvelope", 1, TestJsonContext.Default.TestEnvelope, TestChannel.AnyComposer<TestEvent>()));
        await using var provider = services.Build();

        // Act
        var result = await ProcessAsync(provider, AContext());

        // Assert
        Assert.Equal(Errors.NotFound, result.Error?.Type);
    }

    [Fact]
    public async Task ProcessAsync_HostWithNoLoggers_StoresTheMessages()
    {
        // Arrange
        _doubles.Routes = [ARoute("u1"), ARoute("u2")];
        await using var provider = _doubles.Build(_time, more: static builder => builder.Services.RemoveAll(typeof(ILogger<>))); // the host registers no loggers

        // Act
        var result = await ProcessAsync(provider, AContext());

        // Assert
        Assert.Equal(2, result.Value.Stored);
        _ = _doubles.Store(TenantKey.None).Received(1).AddAsync(OutgoingMessagesTo("u1", "u2"), Ct);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public async Task ProcessAsync_ComposerWithAnotherEnvelopeCountThanRoutes_FailsTheEventWithValidationFailedAndStoresNothing(int envelopeCount)
    {
        // Arrange
        _doubles.Routes = [ARoute("u1"), ARoute("u2")];
        _doubles.Composer(TenantKey.None).ComposeAsync(default!, default, Ct)
            .ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<IReadOnlyList<ErrorOr<TestEnvelope>>>.ForValue(
                [.. Enumerable.Repeat<ErrorOr<TestEnvelope>>(new TestEnvelope("envelope"), envelopeCount)]))); // not one envelope for each route
        var context = AContext();
        await using var provider = _doubles.Build(_time);

        // Act
        var result = await ProcessAsync(provider, context);

        // Assert
        Assert.Equal(Errors.ValidationFailed, result.Error?.Type);
        Assert.Empty(_doubles.Stored);
        _ = _doubles.Composer(TenantKey.None).Received(1).ComposeAsync(ReceivedMessageOf(context), RoutesTo("u1", "u2"), Ct);
        _ = _doubles.Store(TenantKey.None).DidNotReceiveWithAnyArgs().AddAsync(default!, Ct);
    }

    [Fact]
    public async Task ProcessAsync_RouterThatThrows_ReturnsTheExceptionError()
    {
        // Arrange
        _doubles.Router(TenantKey.None).RouteAsync(default!, Ct).ThrowsForAnyArgs(new InvalidOperationException("defect"));
        var context = AContext();
        await using var provider = _doubles.Build(_time);

        // Act
        var result = await ProcessAsync(provider, context);

        // Assert
        Assert.Equal(Errors.Exception, result.Error?.Type);
        _ = _doubles.Router(TenantKey.None).Received(1).RouteAsync(ReceivedMessageOf(context), Ct);
        _ = _doubles.Composer(TenantKey.None).DidNotReceiveWithAnyArgs().ComposeAsync(default!, default, Ct);
        _ = _doubles.Store(TenantKey.None).DidNotReceiveWithAnyArgs().AddAsync(default!, Ct);
    }

    [Fact]
    public async Task ProcessAsync_RegistrationWithRetryExpiry_GivesExpiresAtUtc15MinutesAfterTheFakeTime()
    {
        // Arrange
        _doubles.Routes = [ARoute("u1")];
        await using var provider = _doubles.Build(
            _time, channelRetry: static o => o.Expiry = TimeSpan.FromMinutes(5), withRetry: static o => o.Expiry = TimeSpan.FromMinutes(15));

        // Act
        await ProcessAsync(provider, AContext());

        // Assert
        var delivery = Assert.Single(_doubles.Stored).Delivery;
        Assert.Equal(
            (TestHost.Start.UtcDateTime.AddMinutes(15), 8, TestHost.Start.UtcDateTime),
            (delivery.ExpiresAtUtc, delivery.MaxTryCount, delivery.CreatedAtUtc));
        _ = _doubles.Store(TenantKey.None).Received(1).AddAsync(OutgoingMessagesTo("u1"), Ct);
    }

    [Fact]
    public async Task ProcessAsync_SecondRegistrationOnTheSameChannelWithNoWithRetry_KeepsTheExpiryOfTheChannel()
    {
        // Arrange
        await using var provider = _doubles.Build(
            _time,
            channelRetry: static o => o.Expiry = TimeSpan.FromMinutes(5),
            withRetry: static o => o.Expiry = TimeSpan.FromMinutes(15),
            more: static builder => builder
                .AddMessage("OtherTestEvent", 1, TestJsonContext.Default.OtherTestEvent, static m => m
                    .WithRouter(static (_, _) => PipelineDoubles.RouterGiving<OtherTestEvent>(static () => [ARoute("u1")]))
                    .AsChannel(
                        TestChannel.Id, "OtherTestEnvelope", 1, TestJsonContext.Default.TestEnvelope,
                        static (_, _) => PipelineDoubles.EchoComposer<OtherTestEvent, TestEnvelope>(static r => new TestEnvelope(r.Target.Id)),
                        static o => o.Expiry = TimeSpan.FromMinutes(5))));

        // Act
        await ProcessAsync(provider, new OtherTestEvent("text"), AContext());

        // Assert
        Assert.Equal(TestHost.Start.UtcDateTime.AddMinutes(5), Assert.Single(_doubles.Stored).Delivery.ExpiresAtUtc);
        _ = _doubles.Store(TenantKey.None).Received(1).AddAsync(OutgoingMessagesTo("u1"), Ct);
    }

    private static async Task ProcessOneAfterTheOtherAsync(IServiceProvider provider, IEnumerable<TenantId?> tenants)
    {
        foreach (var tenant in tenants)
        {
            await ProcessAsync(provider, AContext(tenant));
        }
    }
}
