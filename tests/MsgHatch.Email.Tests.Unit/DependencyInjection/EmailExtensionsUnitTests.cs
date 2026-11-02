using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Persistence;
using MsgHatch.Domain.Routing;
using MsgHatch.Email.DependencyInjection;
using MsgHatch.Email.Engines;
using MsgHatch.Email.Templates;
using MsgHatch.Email.Tests.Unit.TestTypes;
using NSubstitute;

namespace MsgHatch.Email.Tests.Unit.DependencyInjection;

/// <summary>The setup methods of the email package (section 4.7), and its startup validation rules.</summary>
public sealed class EmailExtensionsUnitTests(TemplateFolder folder) : IClassFixture<TemplateFolder>
{
    private static readonly TenantKey _t1 = TenantKey.For(new TenantId("t1"));

    public static TheoryData<string, Action<MsgHatchBuilder>> TwoSelectionRows => new()
    {
        { "two shared stores", static builder => builder.UseTestTemplates().UseTemplateStore<EmptyTemplateStore>() },
        { "two tenant stores", static builder => builder.UseTenantTemplateStore<KeyCapturingTemplateStore>().UseTenantTemplateStore<KeyCapturingTemplateStore>() },
    };

    public static TheoryData<string, Action<MsgHatchBuilder>> TemplateStoreRows => new()
    {
        { "a shared store", static builder => builder.UseTestTemplates() },
        { "a tenant store", static builder => builder.UseTenantTemplateStore<KeyCapturingTemplateStore>() },
        { "a shared and a tenant store", static builder => builder.UseTestTemplates().UseTenantTemplateStore<KeyCapturingTemplateStore>() },
    };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AsEmail_Registration_AddsTheContentTypeEmailWithItsName()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatchWithStores().UseTestTemplates().AddTestEvent(static m => m.WithPayload<OrderEmail, TestOrderLoader>(static p => p.AsEmail("OrderEmail")));
        await using var provider = services.Build();

        // Act
        var entries = provider.GetRequiredService<IContentTypeRegistry>().Entries;

        // Assert
        Assert.Equal(
            [("Email:OrderEmail", 1, typeof(OutgoingMessage<EmailMessage>)), ("Settings:Email", 1, typeof(EmailChannelSettings))],
            entries.Where(static entry => entry.ContentType.Contains("Email", StringComparison.Ordinal))
                .Select(static entry => (entry.ContentType, entry.Version, entry.ClrType))
                .OrderBy(static entry => entry.ContentType, StringComparer.Ordinal));
    }

    [Fact]
    public async Task AsEmail_TwoRegistrations_AddTheEmailChannelOneTime()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatchWithStores().UseTestTemplates()
            .AddTestEvent(static m => m.WithPayload<OrderEmail, TestOrderLoader>(static p => p.AsEmail("OrderEmail")))
            .AddMessage("OtherTestEvent", 1, TestJsonContext.Default.OtherTestEvent, static m => m
                .WithRouter<TwoEmailRoutesRouter<OtherTestEvent>>()
                .WithPayload<OrderEmail, OtherOrderLoader>(static p => p.AsEmail("OtherEmail")));
        await using var provider = services.Build();

        // Act
        var channels = provider.GetRequiredService<IMessageRegistry>().Channels;

        // Assert
        Assert.Equal([EmailChannel.Id], channels.Select(static channel => channel.Id));
        Assert.Single(provider.GetRequiredService<IContentTypeRegistry>().Entries, static entry => entry.ContentType == "Settings:Email");
        Assert.Single(provider.GetServices<IChannelSettingsValidator>(), static validator => validator is EmailChannelSettingsValidator);
    }

    [Fact]
    public async Task AsEmail_EngineOfAnotherType_CreatesThatEngineWithTheRequestedKey()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatchWithStores(enableLogging: false)
            .AddTestEvent(static m => m.WithPayload<OrderEmail, TestOrderLoader>(static p => p.AsEmail("OrderEmail", static (services, _) => ActivatorUtilities.CreateInstance<KeyCapturingEngine>(services))));
        await using var provider = services.Build();
        await using var scope = provider.CreateAsyncScope();

        // Act
        var engine = scope.ServiceProvider.GetRequiredKeyedService<IEmailTemplateEngine<OrderEmail>>(_t1);

        // Assert
        Assert.Equal(_t1, Assert.IsType<KeyCapturingEngine>(engine).Key);
    }

    [Fact]
    public async Task UseTemplateStore_StoreType_CreatesOneSharedStoreOfThatType()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatch(static o => o.EnableLogging = false).UseTemplateStore<EmptyTemplateStore>();
        await using var provider = services.Build();
        var first = provider.GetRequiredService<ITemplateStore>();

        // Act
        var second = provider.GetRequiredService<ITemplateStore>();

        // Assert
        Assert.IsType<EmptyTemplateStore>(first);
        Assert.Same(first, second);
    }

    [Fact]
    public async Task UseTenantTemplateStore_StoreType_CreatesTheStoreOfEachKeyWithItsKey()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatch(static o => o.EnableLogging = false).UseTenantTemplateStore<KeyCapturingTemplateStore>();
        await using var provider = services.Build();

        // Act
        var store = provider.GetRequiredKeyedService<ITemplateStore>(_t1);

        // Assert
        Assert.Equal(_t1, Assert.IsType<KeyCapturingTemplateStore>(store).Key);
    }

    [Fact]
    public async Task UseTenantTemplateStore_AndASharedStore_TheUnkeyedStoreIsTheSharedStore()
    {
        // Arrange
        var shared = Substitute.For<ITemplateStore>();
        var services = TestHost.CreateServices();
        services.AddMsgHatch(static o => o.EnableLogging = false)
            .UseTenantTemplateStore(static (_, _) => Substitute.For<ITemplateStore>())
            .UseTemplateStore(_ => shared);
        await using var provider = services.Build();

        // Act
        var store = provider.GetRequiredService<ITemplateStore>();

        // Assert
        Assert.Same(shared, store);
    }

    [Fact]
    public async Task UseEmbeddedTemplates_AssemblyAndPrefix_FindsTheTemplatesOfTheResources()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatch().UseTestTemplates();
        await using var provider = services.Build();

        // Act
        var result = await provider.GetRequiredService<ITemplateStore>().FindAsync(new TemplateLookup(new("TestEvent"), "en", Scope: null), Ct);

        // Assert
        Assert.Equal("Order {{OrderNumber}} shipped", result.Value?.Parts[0].Text);
    }

    [Fact]
    public async Task UseFileTemplates_RootPathFromTheConfiguration_FindsTheTemplatesOfThatFolder()
    {
        // Arrange
        await folder.SeedAsync(Ct);
        var services = TestHost.CreateServices(configuration: new Dictionary<string, string?> { ["MsgHatch:FileTemplates:RootPath"] = folder.Root });
        services.AddMsgHatch().UseFileTemplates();
        await using var provider = services.Build();

        // Act
        var result = await provider.GetRequiredService<ITemplateStore>().FindAsync(new TemplateLookup(new("TestEvent"), "en", Scope: null), Ct);

        // Assert
        Assert.Equal("Order {{OrderNumber}} shipped", result.Value?.Parts[0].Text);
    }

    [Fact]
    public async Task UseFileTemplates_RootPathFromCode_WinsOverTheConfiguration()
    {
        // Arrange
        await folder.SeedAsync(Ct);
        var services = TestHost.CreateServices(
            configuration: new Dictionary<string, string?> { ["MsgHatch:FileTemplates:RootPath"] = Path.Combine(folder.Parent, "missing") });
        services.AddMsgHatch().UseFileTemplates(folder.Root);
        await using var provider = services.Build();

        // Act
        var result = await provider.GetRequiredService<ITemplateStore>().FindAsync(new TemplateLookup(new("TestEvent"), "en", Scope: null), Ct);

        // Assert
        Assert.Equal("Order {{OrderNumber}} shipped", result.Value?.Parts[0].Text);
    }

    [Fact]
    public async Task Validate_FileTemplatesWithNoRootPath_ThrowsOptionsValidationException()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatchWithStores().UseFileTemplates().AddTestEvent(static m => m.WithPayload<OrderEmail, TestOrderLoader>(static p => p.AsEmail("OrderEmail")));
        await using var provider = services.Build();

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        Assert.IsType<OptionsValidationException>(exception);
    }

    [Theory]
    [MemberData(nameof(TemplateStoreRows))]
    public async Task Validate_PlaceholderRegistrationWithATemplateStore_Succeeds(string row, Action<MsgHatchBuilder> templateStores)
    {
        // Arrange
        var services = TestHost.CreateServices();
        var builder = services.AddMsgHatchWithStores();
        templateStores(builder);
        builder.AddTestEvent(static m => m.WithPayload<OrderEmail, TestOrderLoader>(static p => p.AsEmail("OrderEmail")));
        await using var provider = services.Build();

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        Assert.Equal((row, (Exception?)null), (row, exception));
    }

    [Fact]
    public async Task Validate_PlaceholderRegistrationAndNoTemplateStore_ThrowsOptionsValidationException()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatchWithStores().AddTestEvent(static m => m.WithPayload<OrderEmail, TestOrderLoader>(static p => p.AsEmail("OrderEmail")));
        await using var provider = services.Build();

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        Assert.IsType<OptionsValidationException>(exception);
    }

    [Fact]
    public async Task Validate_PlaceholderRegistrationAndNoTemplateStoreForTheRolePublisher_Succeeds()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatch(static o => o.Roles = MsgHatchRoles.Publisher).UseInMemoryStores().UseTransport(static _ => EmailHost.AnEmailTransport())
            .AddTestEvent(static m => m.WithPayload<OrderEmail, TestOrderLoader>(static p => p.AsEmail("OrderEmail")));
        await using var provider = services.Build();

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    public async Task Validate_RegistrationWithAnotherEngineAndNoTemplateStore_Succeeds()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatchWithStores().AddTestEvent(static m => m.WithPayload<OrderEmail, TestOrderLoader>(static p => p.AsEmail("OrderEmail", static (services, _) => ActivatorUtilities.CreateInstance<KeyCapturingEngine>(services))));
        await using var provider = services.Build();

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        Assert.Null(exception);
    }

    [Theory]
    [MemberData(nameof(TwoSelectionRows))]
    public async Task Validate_TwoSelectionsOfATemplateStore_ThrowsOptionsValidationException(string row, Action<MsgHatchBuilder> templateStores)
    {
        // Arrange
        var services = TestHost.CreateServices();
        var builder = services.AddMsgHatchWithStores();
        templateStores(builder);
        builder.AddTestEvent(static m => m.WithPayload<OrderEmail, TestOrderLoader>(static p => p.AsEmail("OrderEmail")));
        await using var provider = services.Build();

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        Assert.Equal((row, typeof(OptionsValidationException)), (row, exception?.GetType()));
    }
}
