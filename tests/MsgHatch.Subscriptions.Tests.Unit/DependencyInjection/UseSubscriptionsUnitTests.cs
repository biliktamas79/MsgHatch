using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Persistence;
using MsgHatch.Domain.Routing;
using MsgHatch.Subscriptions.DependencyInjection;
using MsgHatch.Subscriptions.Management;
using MsgHatch.Subscriptions.Resolution;
using MsgHatch.Subscriptions.Tests.Unit.TestTypes;
using NSubstitute;

namespace MsgHatch.Subscriptions.Tests.Unit.DependencyInjection;

/// <summary>The setup of the subscription router: <c>UseSubscriptions</c> and <c>WithScope</c>, and the startup validation rules of the package.</summary>
public sealed class UseSubscriptionsUnitTests
{
    public static TheoryData<string, Action<MsgHatchBuilder>> WithScopeAndWithTestEventOrderRows => new()
    {
        {
            "WithScope, then WithTestEvent",
            static builder => builder.AddMessage("A", 1, SubscriptionTestJson.Default.EventA, static m => m.WithScope(ScopeOfA).WithTestEvent("basic", ATestEvent))
        },
        {
            "WithTestEvent, then WithScope",
            static builder => builder.AddMessage("A", 1, SubscriptionTestJson.Default.EventA, static m => m.WithTestEvent("basic", ATestEvent).WithScope(ScopeOfA))
        },
    };

    [Fact]
    public async Task Validate_SubscriptionRouterAndNoWithScope_ThrowsOptionsValidationException()
    {
        // Arrange
        var services = TestHost.CreateServices();
        ARoutingHost(services).AddMessage("A", 1, SubscriptionTestJson.Default.EventA, static _ => { });
        await using var provider = services.Build();

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        Assert.IsType<OptionsValidationException>(exception);
    }

    [Fact]
    public async Task Validate_OwnRouterAndNoWithScope_Passes()
    {
        // Arrange
        var services = TestHost.CreateServices();
        ARoutingHost(services).AddMessage(
            "F", 1, SubscriptionTestJson.Default.Notice, static m => m.WithRouter(static (_, _) => Substitute.For<IMessageRouter<Notice>>()));
        await using var provider = services.Build();

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    public async Task Validate_UseSubscriptionsWithNoStoreAndNoDirectory_NamesBothMissingServices()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatch(static o => o.Roles = MsgHatchRoles.Consumer).UseInMemoryStores().UseSubscriptions();
        await using var provider = services.Build();

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        Assert.Equal(
            [
                "UseSubscriptions needs a subscription store, for example UseInMemorySubscriptionStore.",
                "UseSubscriptions needs a recipient directory. Select one with UseRecipientDirectory.",
            ],
            Assert.IsType<OptionsValidationException>(exception).Failures);
    }

    [Fact]
    public async Task UseSubscriptions_TraceType_IsTheContentTypeTraceSubscriptionVersion1OfResolutionTrace()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatch().UseSubscriptions();
        await using var provider = services.Build();

        // Act
        var entries = provider.GetRequiredService<IContentTypeRegistry>().Entries.Where(static entry => entry.ClrType == typeof(ResolutionTrace));

        // Assert
        Assert.Equal([("Trace:Subscription", 1, "Trace:Subscription@1")], entries.Select(static entry => (entry.ContentType, entry.Version, entry.Discriminator)));
    }

    [Theory]
    [MemberData(nameof(WithScopeAndWithTestEventOrderRows))]
    public async Task Validate_WithScopeAndWithTestEventInEachOrder_Passes(string order, Action<MsgHatchBuilder> addMessage)
    {
        // Arrange
        var services = TestHost.CreateServices();
        addMessage(ARoutingHost(services));
        await using var provider = services.Build();

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        Assert.Equal((order, (Exception?)null), (order, exception));
    }

    [Fact]
    public async Task Validate_SubscriptionRouterAndOnlyWithTestEvent_ThrowsOptionsValidationException()
    {
        // Arrange
        var services = TestHost.CreateServices();
        ARoutingHost(services).AddMessage("A", 1, SubscriptionTestJson.Default.EventA, static m => m.WithTestEvent("basic", ATestEvent));
        await using var provider = services.Build();

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        Assert.IsType<OptionsValidationException>(exception);
    }

    [Fact]
    public async Task GetMessageTypesAsync_TestEventOfAMessageTypeWithItsOwnRouter_ListsOnlyTheMessageTypeWithAScopeFunction()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatch(static o => o.Roles = MsgHatchRoles.Consumer)
            .UseInMemoryStores()
            .UseSubscriptions()
            .UseInMemorySubscriptionStore()
            .UseRecipientDirectory(static (_, _) => Substitute.For<IRecipientDirectory>())
            .AddMessage("A", 1, SubscriptionTestJson.Default.EventA, static m => m.WithScope(ScopeOfA))
            .AddMessage("F", 1, SubscriptionTestJson.Default.Notice, static m => m
                .WithRouter(static (_, _) => Substitute.For<IMessageRouter<Notice>>())
                .WithTestEvent("basic", static _ => new Notice("F")));
        await using var provider = services.Build();
        await using var scope = provider.CreateAsyncScope();

        // Act
        var states = await scope.ServiceProvider.GetRequiredService<ISubscriptionQueries>()
            .GetMessageTypesAsync(new MessageTypeQuery(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(["A"], states.Value.Select(static state => state.Type.Name));
    }

    [Theory]
    [InlineData("null test case", null, true, nameof(ArgumentNullException), "testCase")]
    [InlineData("test case of only whitespace", " ", true, nameof(ArgumentException), "testCase")]
    [InlineData("null function", "basic", false, nameof(ArgumentNullException), "create")]
    public void WithTestEvent_InvalidArgument_Throws(string row, string? testCase, bool withFunction, string exceptionType, string parameter)
    {
        // Arrange
        var services = TestHost.CreateServices();
        Func<UserId, EventA>? create = withFunction ? ATestEvent : null;

        // Act
        var exception = Record.Exception(() =>
            services.AddMsgHatch().AddMessage("A", 1, SubscriptionTestJson.Default.EventA, m => m.WithTestEvent(testCase!, create!)));

        // Assert
        Assert.Equal((row, exceptionType, parameter), (row, exception?.GetType().Name, (exception as ArgumentException)?.ParamName));
    }

    private static NotificationScope ScopeOfA(EventA e) => ScenarioEvents.ScopeOf(e.Users, e.Company);

    private static EventA ATestEvent(UserId user) => new([user.Value]);

    private static MsgHatchBuilder ARoutingHost(IServiceCollection services) =>
        services.AddMsgHatch(static o => o.Roles = MsgHatchRoles.Consumer)
            .UseInMemoryStores()
            .UseSubscriptions()
            .UseSubscriptionStore(static (_, _) => Substitute.For<ISubscriptionStore>())
            .UseRecipientDirectory(static (_, _) => Substitute.For<IRecipientDirectory>());
}
