using Amazon.SimpleEmailV2;
using Amazon.SimpleEmailV2.Model;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Outbox;
using MsgHatch.Domain.Routing;
using MsgHatch.Email.AmazonSES.DependencyInjection;
using MsgHatch.Email.Tests.Unit.TestTypes;
using NSubstitute;

namespace MsgHatch.Email.AmazonSES.Tests.Unit;

/// <summary>A publisher host of the email channel only: it sends the emails of a shared outbox, and nothing else (D101).</summary>
public sealed class OutboxDispatcherUnitTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task DispatchPendingAsync_PublisherHostWithOnlyUseAmazonSes_SendsOnlyTheEmailAndKeepsTheOtherMessagePending()
    {
        // Arrange
        var ses = Substitute.For<IAmazonSimpleEmailServiceV2>();
        ses.SendEmailAsync(default!, Ct).ReturnsForAnyArgs(Task.FromResult(new SendEmailResponse { MessageId = "ses-1" }));
        var services = TestHost.CreateServices();
        services.AddSingleton(ses);
        services.AddMsgHatch(static o => o.Roles = MsgHatchRoles.Publisher)
            .UseInMemoryStores()
            .UseAmazonSes(static o => o.SenderEmail = "noreply@example.com");
        await using var provider = services.Build();
        provider.GetRequiredService<IStartupValidator>().Validate();
        var (email, other) = (TestMessages.AnOutgoingEmail(), AnOutgoingMessageOfAnotherChannel());
        await provider.GetRequiredService<IOutboxStore>().AddAsync([email, other], Ct);

        // Act
        await provider.GetRequiredService<IOutboxDispatcher>().DispatchPendingAsync(10, Ct);

        // Assert
        await ses.ReceivedWithAnyArgs(1).SendEmailAsync(default!, Ct);
        Assert.Equal(
            [(email.Delivery.Id, DeliveryStatus.Done, 1), (other.Delivery.Id, DeliveryStatus.Pending, 0)],
            provider.GetRequiredService<InMemoryOutboxStore>().GetMessages()
                .Select(static m => (m.Delivery.Id, m.Delivery.Status, m.Delivery.TryCount)));
    }

    private static OutgoingMessage<string> AnOutgoingMessageOfAnotherChannel()
    {
        var email = TestMessages.AnOutgoingEmail();
        return new OutgoingMessage<string>(
            email.SchemaVersion, email.Delivery with { Id = Guid.NewGuid() }, email.SourceMessageId, email.Tenant, email.Type,
            new ChannelId("SignalR"), new DeliveryTarget(DeliveryTargetKind.User, "u1"), "SignalR:OrderUpdate", 1, MessageFlags.None,
            TraceParent: null, Trace: null, "order 1001 shipped");
    }
}
