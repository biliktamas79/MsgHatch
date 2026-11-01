using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Outbox;
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
        AddTestMessage(services.AddMsgHatch().AddTestChannel().UseInMemoryStores());
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
