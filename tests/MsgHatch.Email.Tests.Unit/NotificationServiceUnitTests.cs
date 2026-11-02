using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Outbox;
using MsgHatch.Domain.Pipeline;
using MsgHatch.Domain.Routing;
using MsgHatch.Email.DependencyInjection;
using MsgHatch.Email.Engines;
using MsgHatch.Email.Tests.Unit.TestTypes;
using NSubstitute;

namespace MsgHatch.Email.Tests.Unit;

/// <summary>The email registration through <see cref="INotificationService.ProcessAsync{TEvent}"/>: steps 3 to 5 of section 4.8 for email.</summary>
public sealed class NotificationServiceUnitTests
{
    private static readonly TenantId _tenant = new("t1");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ProcessAsync_InMemoryStoresEmbeddedTemplatesAndATestRouter_StoresARenderedEmailForEachRouteInTheInMemoryOutbox()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatchWithStores().UseTestTemplates()
            .AddTestEvent(static m => m.WithPayload<OrderEmail, TestOrderLoader>(static p => p.AsEmail("OrderShippedEmail")), name: "OrderShipped");
        await using var provider = services.Build();

        // Act
        var result = await ProcessAsync(provider, new MessageContext(Guid.NewGuid(), _tenant, MessageFlags.None));

        // Assert
        Assert.Equal(2, result.Value.Stored);
        Assert.Equal(
            [
                ((TenantId?)_tenant, new MessageType("OrderShipped"), new DeliveryTarget(DeliveryTargetKind.User, "u1"), "Email:OrderShippedEmail",
                    (RouteTrace?)new TestTrace("TestRouter"), new EmailMessage("client1@example.com", "Order 3 for u1", "<p>Total: 1234.5</p>", "Total: 1234.5")),
                (_tenant, new MessageType("OrderShipped"), new DeliveryTarget(DeliveryTargetKind.User, "u2"), "Email:OrderShippedEmail",
                    new TestTrace("TestRouter"), new EmailMessage("client2@example.com", "u2 rendelése: 3", "<p>Összeg: 1234,5</p>", null)),
            ],
            provider.GetRequiredService<InMemoryOutboxStore>().GetMessages()
                .Select(static message => (message.Tenant, message.Type, message.Target, message.ContentType, message.Trace, ((OutgoingMessage<EmailMessage>)message).Envelope)));
    }

    [Fact]
    public async Task ProcessAsync_EmailWithNoRetryConfiguration_StoresEmailsWithNoExpiry()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatchWithStores().UseTestTemplates()
            .AddTestEvent(static m => m.WithPayload<OrderEmail, TestOrderLoader>(static p => p.AsEmail("OrderShippedEmail")), name: "OrderShipped");
        await using var provider = services.Build();

        // Act
        await ProcessAsync(provider, new MessageContext(Guid.NewGuid(), Tenant: null, MessageFlags.None));

        // Assert
        Assert.Equal([(DateTime?)null, null], provider.GetRequiredService<InMemoryOutboxStore>().GetMessages().Select(static message => message.Delivery.ExpiresAtUtc));
    }

    [Fact]
    public async Task ProcessAsync_EventOfATenant_PreparesOneTemplateForEachGroupWithTheEngineOfItsKey()
    {
        // Arrange
        var prepares = new List<(TenantKey Key, string Language)>();
        var services = TestHost.CreateServices();
        services.AddMsgHatchWithStores()
            .AddTestEvent(m => m.WithPayload<OrderEmail, TestOrderLoader>(p => p.AsEmail("OrderShippedEmail", (_, key) => AnEngineRecordingTo(prepares, key))));
        await using var provider = services.Build();

        // Act
        await ProcessAsync(provider, new MessageContext(Guid.NewGuid(), _tenant, MessageFlags.None));

        // Assert
        Assert.Equal([(TenantKey.For(_tenant), "en"), (TenantKey.For(_tenant), "hu")], prepares);
    }

    private static IEmailTemplateEngine<OrderEmail> AnEngineRecordingTo(List<(TenantKey Key, string Language)> prepares, TenantKey key)
    {
        var engine = Substitute.For<IEmailTemplateEngine<OrderEmail>>();
        engine.PrepareAsync(default, Ct).ReturnsForAnyArgs(call =>
        {
            prepares.Add((key, call.ArgAt<EmailTemplateRequest>(0).Language));
            return ValueTask.FromResult<ErrorOr<IEmailTemplate<OrderEmail>>>(new Error(Errors.NotFound));
        });
        return engine;
    }

    private static async Task<ErrorOr<NotificationResult>> ProcessAsync(IServiceProvider provider, MessageContext context)
    {
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<INotificationService>().ProcessAsync(new TestEvent("1001"), context, Ct);
    }
}
