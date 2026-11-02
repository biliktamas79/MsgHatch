using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Outbox;
using MsgHatch.Domain.Pipeline;
using MsgHatch.Email.DependencyInjection;
using MsgHatch.Email.Tests.Unit.TestTypes;
using NSubstitute;

namespace MsgHatch.Email.Tests.Unit;

/// <summary>
/// The email channel from the event to the email client, in one host with both roles: the pipeline renders the
/// templates into the outbox, and the dispatcher sends each rendered email through <see cref="IEmailClient"/>
/// (section 4.8, steps 1 to 7). The email client is the only substitute.
/// </summary>
public sealed class OutboxDispatcherUnitTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task DispatchPendingAsync_EventThatThePipelineProcessedWithEmailTemplates_SendsEachRenderedEmailThroughTheEmailClient()
    {
        // Arrange
        var client = Substitute.For<IEmailClient>();
        client.SendAsync(default, default!, Ct).ReturnsForAnyArgs(ValueTask.FromResult<ErrorOr<PublishReceipt>>(new PublishReceipt("provider-1")));
        var services = TestHost.CreateServices();
        services.AddMsgHatch().UseInMemoryStores().UseTestTemplates().UseEmailClient(_ => client)
            .AddTestEvent(static m => m.WithPayload<OrderEmail, TestOrderLoader>(static p => p.AsEmail("OrderShippedEmail")), name: "OrderShipped");
        await using var provider = services.Build();
        provider.GetRequiredService<IStartupValidator>().Validate();
        await ProcessAsync(provider, new MessageContext(Guid.NewGuid(), new TenantId("t1"), MessageFlags.None));

        // Act
        await provider.GetRequiredService<IOutboxDispatcher>().DispatchPendingAsync(10, Ct);

        // Assert
        Assert.Equal(
            [
                new EmailMessage("client1@example.com", "Order 3 for u1", "<p>Total: 1234.5</p>", "Total: 1234.5"),
                new EmailMessage("client2@example.com", "u2 rendelése: 3", "<p>Összeg: 1234,5</p>", null),
            ],
            client.ReceivedCalls().Select(static call => (EmailMessage)call.GetArguments()[1]!).OrderBy(static email => email.To, StringComparer.Ordinal));
    }

    private static async Task ProcessAsync(IServiceProvider provider, MessageContext context)
    {
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<INotificationService>().ProcessAsync(new TestEvent("1001"), context, Ct);
    }
}
