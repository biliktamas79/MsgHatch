using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Outbox;
using MsgHatch.Domain.Pipeline;
using MsgHatch.Email.DependencyInjection;
using MsgHatch.Email.Razor.DependencyInjection;
using MsgHatch.Email.Razor.Tests.Unit.TestTypes;
using MsgHatch.Email.Tests.Unit.TestTypes;

namespace MsgHatch.Email.Razor.Tests.Unit.DependencyInjection;

/// <summary>The setup methods of the Razor engine (section 4.7), their startup validation, and a Razor email through the pipeline.</summary>
public sealed class RazorEmailExtensionsUnitTests
{
    private static readonly Assembly _templates = typeof(RazorTemplateTestData).Assembly;

    public static TheoryData<string, Action<MsgHatchBuilder>> FailingSetupRows => new()
    {
        { "no UseRazorEmailTemplates", static builder => builder.UseTestTemplates().AddTestEvent(RazorOrderEmail) },
        { "two UseRazorEmailTemplates", static builder => builder.UseTestTemplates().UseRazorTestTemplates().UseRazorTestTemplates().AddTestEvent(RazorOrderEmail) },
        { "no template store", static builder => builder.UseRazorTestTemplates().AddTestEvent(RazorOrderEmail) },
        {
            "the placeholder engine and the Razor engine for one payload type",
            static builder => builder.UseTestTemplates().UseRazorTestTemplates()
                .AddTestEvent(static m => m.WithPayload<OrderEmail, TestOrderLoader>(static p => p.AsEmail("OrderEmail")))
                .AddMessage("OtherTestEvent", 1, TestJsonContext.Default.OtherTestEvent, static m => m
                    .WithRouter<TwoEmailRoutesRouter<OtherTestEvent>>()
                    .WithPayload<OrderEmail, OtherOrderLoader>(static p => p.AsRazorEmail("OtherEmail")))
        },
    };

    public static TheoryData<string, Action<MsgHatchBuilder>> PassingSetupRows => new()
    {
        {
            "two Razor registrations of one payload type",
            static builder => builder.UseTestTemplates().UseRazorTestTemplates()
                .AddTestEvent(RazorOrderEmail)
                .AddMessage("OtherTestEvent", 1, TestJsonContext.Default.OtherTestEvent, static m => m
                    .WithRouter<TwoEmailRoutesRouter<OtherTestEvent>>()
                    .WithPayload<OrderEmail, OtherOrderLoader>(static p => p.AsRazorEmail("OtherEmail")))
        },
        { "a template store of each tenant", static builder => builder.UseTenantTemplateStore<KeyCapturingTemplateStore>().UseRazorTestTemplates().AddTestEvent(RazorOrderEmail) },
    };

    public static TheoryData<string?, Type> RootNamespaceRows => new()
    {
        { null, typeof(ArgumentNullException) },
        { "", typeof(ArgumentException) },
        { " ", typeof(ArgumentException) },
    };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [MemberData(nameof(FailingSetupRows))]
    public async Task Validate_IncompleteOrAmbiguousRazorSetup_ThrowsOptionsValidationException(string row, Action<MsgHatchBuilder> setup)
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
    public async Task Validate_CompleteRazorSetup_Succeeds(string row, Action<MsgHatchBuilder> setup)
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
    public async Task Validate_PublisherWithARazorRegistrationAndNoTemplates_Succeeds()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatch(static o => o.Roles = MsgHatchRoles.Publisher).UseInMemoryStores().UseTransport(static _ => EmailHost.AnEmailTransport())
            .AddTestEvent(RazorOrderEmail);
        await using var provider = services.Build();

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        Assert.Null(exception);
    }

    [Theory]
    [MemberData(nameof(RootNamespaceRows))]
    public void UseRazorEmailTemplates_InvalidRootNamespace_Throws(string? rootNamespace, Type expectedException)
    {
        // Arrange
        var builder = TestHost.CreateServices().AddMsgHatch();

        // Act
        var exception = Record.Exception(() => builder.UseRazorEmailTemplates(_templates, rootNamespace!));

        // Assert
        Assert.IsType(expectedException, exception, exactMatch: true);
    }

    [Fact]
    public void UseRazorEmailTemplates_NoAssembly_ThrowsArgumentNullException()
    {
        // Arrange
        var builder = TestHost.CreateServices().AddMsgHatch();

        // Act
        var exception = Record.Exception(() => builder.UseRazorEmailTemplates(null!, RazorTemplateTestData.Root));

        // Assert
        Assert.IsType<ArgumentNullException>(exception);
    }

    [Fact]
    public async Task ProcessAsync_RazorRegistrationWithEmbeddedTemplates_StoresTheSubjectOfTheStoreAndTheHtmlOfTheComponent()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatchWithStores()
            .UseEmbeddedTemplates(_templates, RazorTemplateTestData.Root)
            .UseRazorTestTemplates()
            .AddTestEvent(RazorOrderEmail, name: "RazorOrder");
        await using var provider = services.Build();
        await using var scope = provider.CreateAsyncScope();

        // Act
        var result = await scope.ServiceProvider.GetRequiredService<INotificationService>()
            .ProcessAsync(new TestEvent("1001"), new MessageContext(Guid.NewGuid(), Tenant: null, MessageFlags.None), Ct);

        // Assert
        Assert.Equal(2, result.Value.Stored);
        Assert.Equal(
            [
                new EmailMessage("client1@example.com", "Order 3 for u1", "<p>Total: 1234.5</p>", "Total: 1234.5"),
                new EmailMessage("client2@example.com", "u2 rendelése: 3", "<p>Összeg: 1234,5</p>", null),
            ],
            provider.GetRequiredService<InMemoryOutboxStore>().GetMessages().Select(static message => ((OutgoingMessage<EmailMessage>)message).Envelope));
    }

    private static void RazorOrderEmail(MessageBuilder<TestEvent> message) =>
        message.WithPayload<OrderEmail, TestOrderLoader>(static p => p.AsRazorEmail("OrderEmail"));
}

/// <summary>The Razor templates of the folder <c>EmailTemplates</c> of this project.</summary>
internal static class RazorTestSetup
{
    public static MsgHatchBuilder UseRazorTestTemplates(this MsgHatchBuilder builder) =>
        builder.UseRazorEmailTemplates(typeof(RazorTemplateTestData).Assembly, RazorTemplateTestData.Root);
}
