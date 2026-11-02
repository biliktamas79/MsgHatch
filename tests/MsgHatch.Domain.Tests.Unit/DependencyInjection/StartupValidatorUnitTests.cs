using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Outbox;
using MsgHatch.Domain.Pipeline;
using MsgHatch.Domain.Routing;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using NSubstitute;

namespace MsgHatch.Domain.Tests.Unit.DependencyInjection;

/// <summary>The startup validation of section 4.7 for the role of an event consumer, through <see cref="IStartupValidator"/>.</summary>
public sealed class StartupValidatorUnitTests
{
    [Fact]
    public async Task Validate_CompleteSetup_Succeeds()
    {
        // Arrange
        var services = TestHost.CreateServices();
        AddTestMessage(services.AddMsgHatch().AddTestChannel().UseInMemoryStores().UseTransport(static _ => TestTransports.For(TestChannel.Id)));
        await using var provider = services.Build();

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    public async Task Validate_TwoSelectionsOfAnImplementationForTheSameInterface_ThrowsOptionsValidationException()
    {
        // Arrange
        var services = TestHost.CreateServices();
        AddTestMessage(services.AddMsgHatch().AddTestChannel().UseInMemoryStores()
            .UseOutboxStore(static (_, _) => Substitute.For<IOutboxStore>())); // a second selection of IOutboxStore (D62)
        await using var provider = services.Build();

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        Assert.IsType<OptionsValidationException>(exception);
    }

    [Fact]
    public async Task Validate_MessageTypeWithNoRouter_ThrowsOptionsValidationException()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatch().AddTestChannel().UseInMemoryStores()
            .AddMessage("TestEvent", 2, TestJsonContext.Default.TestEvent, static m => m // no router
                .AsChannel(TestChannel.Id, "TestEnvelope", 1, TestJsonContext.Default.TestEnvelope, TestChannel.AnyComposer<TestEvent>()));
        await using var provider = services.Build();

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        Assert.IsType<OptionsValidationException>(exception);
    }

    [Fact]
    public async Task Validate_RouterThatDoesNotResolve_ThrowsOptionsValidationException()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatch().AddTestChannel().UseInMemoryStores()
            .AddMessage("TestEvent", 2, TestJsonContext.Default.TestEvent, static m => m
                .WithRouter<RouterWithAMissingService>() // its constructor parameter has no registration
                .AsChannel(TestChannel.Id, "TestEnvelope", 1, TestJsonContext.Default.TestEnvelope, TestChannel.AnyComposer<TestEvent>()));
        await using var provider = services.Build();

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        Assert.IsType<OptionsValidationException>(exception);
    }

    [Fact]
    public async Task Validate_OpenRouterWithAConstraintThatTheEventTypeBreaks_ThrowsOptionsValidationException()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatch().AddTestChannel().UseInMemoryStores()
            .UseRouter(typeof(ConstrainedTestOpenRouter<>)) // TestEvent is not IDisposable
            .AddMessage("TestEvent", 2, TestJsonContext.Default.TestEvent, static m => m
                .AsChannel(TestChannel.Id, "TestEnvelope", 1, TestJsonContext.Default.TestEnvelope, TestChannel.AnyComposer<TestEvent>()));
        await using var provider = services.Build();

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        Assert.IsType<OptionsValidationException>(exception);
    }

    [Fact]
    public async Task Validate_NoOutboxStores_ThrowsOptionsValidationException()
    {
        // Arrange
        var services = TestHost.CreateServices();
        AddTestMessage(services.AddMsgHatch().AddTestChannel()); // no outbox stores
        await using var provider = services.Build();

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        Assert.IsType<OptionsValidationException>(exception);
    }

    [Fact]
    public async Task Validate_ContentTypeAndVersionTwoTimes_ThrowsOptionsValidationException()
    {
        // Arrange
        var services = TestHost.CreateServices();
        AddTestMessage(services.AddMsgHatch().AddTestChannel().UseInMemoryStores()
            .AddTraceType("Test", 1, TestJsonContext.Default.TestTrace)
            .AddTraceType("Test", 1, TestJsonContext.Default.TestTrace)); // the same content type and version again
        await using var provider = services.Build();

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        Assert.IsType<OptionsValidationException>(exception);
    }

    [Fact]
    public async Task Validate_TwoMessageTypesWithTheSameNameAndOtherVersions_ThrowsOptionsValidationException()
    {
        // Arrange
        var services = TestHost.CreateServices();
        AddTestMessage(services.AddMsgHatch().AddTestChannel().UseInMemoryStores()
            .AddMessage("TestEvent", 1, TestJsonContext.Default.OtherTestEvent, static m => m // the name of the test message again
                .WithRouter(static (_, _) => Substitute.For<IMessageRouter<OtherTestEvent>>())
                .AsChannel(TestChannel.Id, "OtherEnvelope", 1, TestJsonContext.Default.TestEnvelope, TestChannel.AnyComposer<OtherTestEvent>())));
        await using var provider = services.Build();

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        Assert.IsType<OptionsValidationException>(exception);
    }

    [Fact]
    public async Task Validate_TransportsForTwoChannels_Succeeds()
    {
        // Arrange
        var services = TestHost.CreateServices();
        AddTestMessage(services.AddMsgHatch().AddTestChannel().UseInMemoryStores()
            .UseTransport(static _ => TestTransports.For(TestChannel.Id))
            .UseTransport(static _ => TestTransports.For(TestChannel.SecondId)));
        await using var provider = services.Build();

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    public async Task Validate_SecondTransportForTheSameChannel_ThrowsOptionsValidationException()
    {
        // Arrange
        var services = TestHost.CreateServices();
        AddTestMessage(services.AddMsgHatch().AddTestChannel().UseInMemoryStores()
            .UseTransport(static _ => TestTransports.For(TestChannel.Id))
            .UseTransport(static _ => TestTransports.For(TestChannel.Id))); // one transport for each channel (D129)
        await using var provider = services.Build();

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        Assert.IsType<OptionsValidationException>(exception);
    }

    [Fact]
    public async Task Validate_TwoSelectionsOfADispatchSignal_ThrowsOptionsValidationException()
    {
        // Arrange
        var services = TestHost.CreateServices();
        AddTestMessage(services.AddMsgHatch().AddTestChannel().UseInMemoryStores()
            .UseDispatchSignal<InProcessDispatchSignal>()
            .UseDispatchSignal(static _ => Substitute.For<IDispatchSignal>())); // a second selection of IDispatchSignal (D62)
        await using var provider = services.Build();

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        Assert.IsType<OptionsValidationException>(exception);
    }

    public static TheoryData<string> RegistrationsThatBreakD135 =>
    [
        "SecondRegistrationOnTheSameChannel",
        "TwoChannelRegistrationsWithTheSameEnvelopeType",
        "TwoLoaderTypesForOnePayloadType",
        "TwoPayloadRegistrationsWithTwoLoaderTypesForOnePayloadType",
    ];

    [Theory]
    [MemberData(nameof(RegistrationsThatBreakD135))]
    public async Task Validate_ChannelRegistrationsOrLoadersThatBreakD135_ThrowsOptionsValidationException(string registrations)
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatch().AddTestChannel().AddSecondTestChannel().UseInMemoryStores()
            .UseTransport(static _ => TestTransports.For(TestChannel.Id))
            .AddMessage("TestEvent", 2, TestJsonContext.Default.TestEvent, m => Register(m.WithRouter<TwoRouteTestRouter>(), registrations));
        await using var provider = services.Build();

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        Assert.IsType<OptionsValidationException>(exception);
    }

    [Fact]
    public async Task Validate_TwoChannelRegistrationsWithOnePayloadTypeAndOneLoaderType_Succeeds()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatch().AddTestChannel().AddSecondTestChannel().UseInMemoryStores()
            .UseTransport(static _ => TestTransports.For(TestChannel.Id))
            .AddMessage("TestEvent", 2, TestJsonContext.Default.TestEvent, static m => m
                .WithRouter<TwoRouteTestRouter>()
                .UsePayloadLoader<TestPayload, TestPayloadLoader>()
                .AsChannel<TestEnvelope, PayloadTestComposer>(TestChannel.Id, "TestEnvelope", 1, TestJsonContext.Default.TestEnvelope)
                .UsePayloadLoader<TestPayload, TestPayloadLoader>()
                .AsChannel<OtherTestEnvelope, OtherPayloadTestComposer>(TestChannel.SecondId, "OtherEnvelope", 1, TestJsonContext.Default.OtherTestEnvelope));
        await using var provider = services.Build();

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        Assert.Null(exception);
    }

    private static MessageBuilder<TestEvent> Register(MessageBuilder<TestEvent> m, string registrations) => registrations switch
    {
        "SecondRegistrationOnTheSameChannel" => m
            .AsChannel(TestChannel.Id, "First", 1, TestJsonContext.Default.TestEnvelope, TestChannel.AnyComposer<TestEvent>())
            .AsChannel(TestChannel.Id, "Second", 1, TestJsonContext.Default.OtherTestEnvelope,
                static (_, _) => Substitute.For<IEnvelopeComposer<TestEvent, OtherTestEnvelope>>()),
        "TwoChannelRegistrationsWithTheSameEnvelopeType" => m
            .AsChannel(TestChannel.Id, "First", 1, TestJsonContext.Default.TestEnvelope, TestChannel.AnyComposer<TestEvent>())
            .AsChannel(TestChannel.SecondId, "Second", 1, TestJsonContext.Default.TestEnvelope, TestChannel.AnyComposer<TestEvent>()),
        "TwoPayloadRegistrationsWithTwoLoaderTypesForOnePayloadType" => m
            .WithPayload<TestPayload, TestPayloadLoader>(static p => p
                .AsChannel<TestEnvelope, PayloadTestComposer>(TestChannel.Id, "First", 1, TestJsonContext.Default.TestEnvelope))
            .WithPayload<TestPayload, OtherTestPayloadLoader>(static p => p
                .AsChannel<OtherTestEnvelope, OtherPayloadTestComposer>(TestChannel.SecondId, "Second", 1, TestJsonContext.Default.OtherTestEnvelope)),
        _ => m
            .UsePayloadLoader<TestPayload, TestPayloadLoader>()
            .UsePayloadLoader<TestPayload, OtherTestPayloadLoader>()
            .AsChannel(TestChannel.Id, "First", 1, TestJsonContext.Default.TestEnvelope, TestChannel.AnyComposer<TestEvent>()),
    };

    private sealed class RouterWithAMissingService(IComparer<Guid> missing) : IMessageRouter<TestEvent>
    {
        public ValueTask<ErrorOr<IReadOnlyList<MessageRoute>>> RouteAsync(
            ReceivedMessage<TestEvent> message, CancellationToken ct) =>
            ValueTask.FromResult(ErrorOr<IReadOnlyList<MessageRoute>>.ForValue([]));

        public IComparer<Guid> Missing { get; } = missing;
    }

    private static void AddTestMessage(MsgHatchBuilder builder) =>
        builder.AddMessage("TestEvent", 2, TestJsonContext.Default.TestEvent, static m => m
            .WithRouter<TwoRouteTestRouter>()
            .AsChannel(TestChannel.Id, "TestEnvelope", 1, TestJsonContext.Default.TestEnvelope, TestChannel.AnyComposer<TestEvent>()));
}
