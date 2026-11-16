using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MsgHatch.Templates.DependencyInjection;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Outbox;
using MsgHatch.Domain.Pipeline;
using MsgHatch.Email;
using MsgHatch.Email.DependencyInjection;
using MsgHatch.Templates.Liquid;
using MsgHatch.Templates.Liquid.Tests.Unit.TestTypes;
using MsgHatch.Email.Tests.Unit.TestTypes;
using MsgHatch.Templates.Tests.Unit.TestTypes;

namespace MsgHatch.Templates.Liquid.Tests.Unit;

/// <summary>The engine factory of the Liquid engine with <c>AsEmail</c> (D216), its startup validation, and a Liquid email through the pipeline.</summary>
public sealed class LiquidTemplatesUnitTests
{
    public static TheoryData<string, Action<MsgHatchBuilder>> FailingSetupRows => new()
    {
        { "no template store", static builder => builder.AddTestEvent(LiquidOrderEmail) },
        {
            "the placeholder engine and the Liquid engine for one payload type",
            static builder => builder.UseTestTemplates()
                .AddTestEvent(static m => m.WithPayload<OrderEmail, TestOrderLoader>(static p => p.AsEmail("OrderEmail")))
                .AddMessage("OtherTestEvent", 1, TestJsonContext.Default.OtherTestEvent, static m => m
                    .WithRouter<TwoEmailRoutesRouter<OtherTestEvent>>()
                    .WithPayload<OrderEmail, OtherOrderLoader>(static p => p.AsEmail("OtherEmail", LiquidTemplates.Engine)))
        },
    };

    public static TheoryData<string, Action<MsgHatchBuilder>> PassingSetupRows => new()
    {
        { "a shared template store", static builder => builder.UseTestTemplates().AddTestEvent(LiquidOrderEmail) },
        { "a template store of each tenant", static builder => builder.UseTenantTemplateStore<KeyCapturingTemplateStore>().AddTestEvent(LiquidOrderEmail) },
        {
            "two Liquid registrations of one payload type",
            static builder => builder.UseTestTemplates()
                .AddTestEvent(LiquidOrderEmail)
                .AddMessage("OtherTestEvent", 1, TestJsonContext.Default.OtherTestEvent, static m => m
                    .WithRouter<TwoEmailRoutesRouter<OtherTestEvent>>()
                    .WithPayload<OrderEmail, OtherOrderLoader>(static p => p.AsEmail("OtherEmail", LiquidTemplates.Engine)))
        },
    };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [MemberData(nameof(FailingSetupRows))]
    public async Task Validate_IncompleteOrAmbiguousLiquidSetup_ThrowsOptionsValidationException(string row, Action<MsgHatchBuilder> setup)
    {
        // Arrange
        var services = TestHost.CreateServices();
        setup(services.AddMsgHatchWithStores());
        await using var provider = services.Build();

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        Assert.Equal((row, typeof(OptionsValidationException)), (row, exception?.GetType()));
    }

    [Theory]
    [MemberData(nameof(PassingSetupRows))]
    public async Task Validate_CompleteLiquidSetup_Succeeds(string row, Action<MsgHatchBuilder> setup)
    {
        // Arrange
        var services = TestHost.CreateServices();
        setup(services.AddMsgHatchWithStores());
        await using var provider = services.Build();

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        Assert.Equal((row, (Type?)null), (row, exception?.GetType()));
    }

    [Fact]
    public async Task Validate_PublisherWithALiquidRegistrationAndNoTemplateStore_Succeeds()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatch(static o => o.Roles = MsgHatchRoles.Publisher).UseInMemoryStores().UseTransport(static _ => EmailHost.AnEmailTransport())
            .AddTestEvent(LiquidOrderEmail);
        await using var provider = services.Build();

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    public async Task ProcessAsync_LiquidRegistrationWithEmbeddedTemplates_StoresAnEmailForEachRouteWithALoopAndACondition()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatchWithStores()
            .UseEmbeddedTemplates(typeof(LiquidTemplateTestData).Assembly, LiquidTemplateTestData.Prefix)
            .AddTestEvent(
                static m => m.WithPayload<LineOrderEmail, LineOrderLoader<TestEvent>>(static p => p.AsEmail("LiquidOrder", LiquidTemplates.Engine)),
                name: "LiquidOrder");
        await using var provider = services.Build();
        await using var scope = provider.CreateAsyncScope();

        // Act
        var result = await scope.ServiceProvider.GetRequiredService<INotificationService>()
            .ProcessAsync(new TestEvent("1001"), new MessageContext(Guid.NewGuid(), Tenant: null, MessageFlags.None), Ct);

        // Assert
        Assert.Equal(2, result.Value.Stored);
        Assert.Equal(
            [
                new EmailMessage(
                    "client1@example.com", "Order of u1", "<ul><li>Pump x2</li><li>Valve x1</li></ul><p>Leave it at the door</p>", "2 lines"),
                new EmailMessage("client2@example.com", "u2 rendelése", "<ul><li>Pump: 2 db</li><li>Valve: 1 db</li></ul>", "2 lines"),
            ],
            provider.GetRequiredService<InMemoryOutboxStore>().GetMessages().Select(static message => ((OutgoingMessage<EmailMessage>)message).Envelope));
    }

    private static void LiquidOrderEmail(MessageBuilder<TestEvent> message) =>
        message.WithPayload<OrderEmail, TestOrderLoader>(static p => p.AsEmail("OrderEmail", LiquidTemplates.Engine));
}
