using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Outbox;
using MsgHatch.Domain.Persistence;
using MsgHatch.Domain.Pipeline;
using MsgHatch.Domain.Routing;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using MsgHatch.SignalR.DependencyInjection;
using MsgHatch.SignalR.Tests.Unit.TestTypes;
using NSubstitute;

namespace MsgHatch.SignalR.Tests.Unit.DependencyInjection;

/// <summary>The setup methods of the SignalR package (section 4.7): <c>AsSignalR</c> and <c>UseSignalR</c>.</summary>
public sealed class SignalRExtensionsUnitTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AsSignalR_Registration_AddsTheContentTypeSignalRWithItsNameAndVersion()
    {
        // Arrange
        var services = SignalRHost.CreateServices();
        services.AddMsgHatch().UseInMemoryStores().UseSignalR<TestHub>().AddTestEvent();
        await using var provider = services.Build();

        // Act
        var entries = provider.GetRequiredService<IContentTypeRegistry>().Entries;

        // Assert
        Assert.Equal(
            [("SignalR:TestUpdate", 2, typeof(OutgoingMessage<SignalREnvelope<TestPayload>>)), ("Settings:SignalR", 1, typeof(SignalRChannelSettings))],
            entries.Where(static entry => entry.ContentType.Contains("SignalR", StringComparison.Ordinal))
                .Select(static entry => (entry.ContentType, entry.Version, entry.ClrType))
                .OrderByDescending(static entry => entry.ContentType, StringComparer.Ordinal));
    }

    [Fact]
    public async Task ProcessAsync_SignalRWithNoRetryConfiguration_StoresEnvelopesWith3TriesThatExpire5MinutesAfterTheFakeTime()
    {
        // Arrange
        var services = SignalRHost.CreateServices();
        services.AddMsgHatch().UseInMemoryStores().UseSignalR<TestHub>().AddTestEvent();
        await using var provider = services.Build();

        // Act
        await using (var scope = provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<INotificationService>()
                .ProcessAsync(new TestEvent("1001"), new MessageContext(Guid.NewGuid(), Tenant: null, MessageFlags.None), Ct);
        }

        // Assert
        var expiresAtUtc = TestHost.Start.UtcDateTime.AddMinutes(5);
        Assert.Equal(
            [
                (new DeliveryTarget(DeliveryTargetKind.User, "u1"), "SignalR:TestUpdate", 3, (DateTime?)expiresAtUtc, new SignalREnvelope<TestPayload>(new TestPayload("u1"))),
                (new DeliveryTarget(DeliveryTargetKind.Company, "c1"), "SignalR:TestUpdate", 3, expiresAtUtc, new SignalREnvelope<TestPayload>(new TestPayload("c1"))),
            ],
            provider.GetRequiredService<InMemoryOutboxStore>().GetMessages().Select(static message => (
                message.Target, message.ContentType, message.Delivery.MaxTryCount, message.Delivery.ExpiresAtUtc,
                ((OutgoingMessage<SignalREnvelope<TestPayload>>)message).Envelope)));
    }

    [Fact]
    public async Task UseSignalR_Alone_RegistersOneTransportOfTheSignalRChannelThatSendsEachPayloadType()
    {
        // Arrange
        var hub = new HubDoubles();
        var services = SignalRHost.CreateServices();
        services.AddSingleton(hub.Hub);
        services.AddMsgHatch().UseInMemoryStores().UseSignalR<TestHub>();
        await using var provider = services.Build();
        var transport = Assert.Single(provider.GetServices<ITransport>());
        var (first, second) = (SignalRMessages.AnOutgoingMessage(), SignalRMessages.AnOutgoingMessage(new OtherPayload(7)));

        // Act
        await transport.SendAsync(first, redirectTo: null, Ct);
        await transport.SendAsync(second, redirectTo: null, Ct);

        // Assert
        Assert.Equal(SignalRChannel.Id, transport.Channel);
        Assert.Equal(
            [first.Envelope.Payload, second.Envelope.Payload],
            hub.Proxy.ReceivedCalls().Select(static call => ((object?[])call.GetArguments()[1]!).Single()));
    }

    [Fact]
    public async Task UseSignalR_Alone_RegistersTheHubContextOfTheHubAndPassesTheStartupValidation()
    {
        // Arrange
        var services = SignalRHost.CreateServices();
        services.AddMsgHatch(static o => o.Roles = MsgHatchRoles.Publisher).UseInMemoryStores().UseSignalR<TestHub>();
        await using var provider = services.Build();

        // Act
        var validation = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);
        var result = await provider.GetRequiredService<ITransport>().SendAsync(SignalRMessages.AnOutgoingMessage(), redirectTo: null, Ct);

        // Assert
        Assert.NotNull(provider.GetService<IHubContext<TestHub>>());
        Assert.Null(validation);
        Assert.Equal(new PublishReceipt(ProviderMessageId: null), result.Value);
    }

    [Fact]
    public async Task UseSignalR_PublisherHostWithNoMessageType_AppliesTheTestDeliveryOfTheConfiguration()
    {
        // Arrange
        var hub = new HubDoubles();
        var services = SignalRHost.CreateServices(new Dictionary<string, string?> { ["MsgHatch:TestDelivery:SignalR:Mode"] = "Deliver" });
        services.AddSingleton(hub.Hub);
        services.AddMsgHatch(static o => o.Roles = MsgHatchRoles.Publisher).UseInMemoryStores().UseSignalR<TestHub>();
        await using var provider = services.Build();
        await provider.GetRequiredService<IOutboxStore>().AddAsync([SignalRMessages.AnOutgoingMessage(flags: MessageFlags.Test)], Ct);

        // Act
        await provider.GetRequiredService<IOutboxDispatcher>().DispatchPendingAsync(10, Ct);

        // Assert
        await hub.Proxy.ReceivedWithAnyArgs(1).SendCoreAsync(default!, default!, Ct);
    }
}
