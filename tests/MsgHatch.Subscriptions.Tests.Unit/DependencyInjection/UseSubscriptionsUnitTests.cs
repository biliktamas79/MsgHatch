using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Persistence;
using MsgHatch.Domain.Routing;
using MsgHatch.Subscriptions.DependencyInjection;
using MsgHatch.Subscriptions.Resolution;
using MsgHatch.Subscriptions.Tests.Unit.TestTypes;
using NSubstitute;

namespace MsgHatch.Subscriptions.Tests.Unit.DependencyInjection;

/// <summary>The setup of the subscription router: <c>UseSubscriptions</c> and <c>WithScope</c>, and the startup validation rules of the package.</summary>
public sealed class UseSubscriptionsUnitTests
{
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

    private static MsgHatchBuilder ARoutingHost(IServiceCollection services) =>
        services.AddMsgHatch(static o => o.Roles = MsgHatchRoles.Consumer)
            .UseInMemoryStores()
            .UseSubscriptions()
            .UseSubscriptionStore(static (_, _) => Substitute.For<ISubscriptionStore>())
            .UseRecipientDirectory(static (_, _) => Substitute.For<IRecipientDirectory>());
}
