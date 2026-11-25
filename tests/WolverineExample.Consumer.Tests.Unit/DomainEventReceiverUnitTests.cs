using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Channels.Email;
using MsgHatch.Channels.SignalR;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Outbox;
using MsgHatch.Domain.Routing;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using Wolverine;
using WolverineExample.Consumer.Notifications;
using WolverineExample.Consumer.Tests.Unit.TestTypes;
using WolverineExample.Contracts;
using static WolverineExample.Consumer.Tests.Unit.TestTypes.ConsumerTestHost;

namespace WolverineExample.Consumer.Tests.Unit;

/// <summary>
/// The routing of the five domain events of the consumer: the Wolverine handler accepts each event into the inbox of a
/// consumer host on a SQLite file with the seeded scenario, the inbox processor routes it with the subscriptions, and
/// the test reads the messages that the outbox stores for the email and the SignalR channel.
/// </summary>
public sealed class DomainEventReceiverUnitTests : IAsyncLifetime
{
    private static readonly DateTimeOffset _now = TestHost.Start;
    private ConsumerTestHost _host = null!;
    private DomainEventReceiver _sut = null!;

    public async ValueTask InitializeAsync()
    {
        _host = await ConsumerTestHost.StartAsync();
        _sut = _host.Receiver;
    }

    public ValueTask DisposeAsync() => _host.DisposeAsync();

    [Fact]
    public async Task Handle_TenantMaintenance_GivesEachSignalRNotificationTheIdOfItsOutgoingMessage()
    {
        // Arrange
        var maintenance = new TenantMaintenance("north", _now.AddDays(1), _now.AddDays(1).AddHours(2));

        // Act
        await _sut.Handle(maintenance, new Envelope(), Ct);

        // Assert
        var messages = await SignalRMessagesAsync();
        Assert.Equal(2, messages.Count);
        Assert.Equal(
            [.. messages.Select(static message => message.Delivery.Id)],
            [.. messages.Select(static message => ((SignalREnvelope<Notification>)message.Content).Payload.Id)]);
    }

    [Fact]
    public async Task Handle_UserLogin_SendsSignalRToTheUserAndEmailToTheAdminOfItsCompany()
    {
        // Arrange
        var login = new UserLogin("north", 1, _now);

        // Act
        await _sut.Handle(login, new Envelope(), Ct);

        // Assert
        Assert.Equal(
            [
                "north Email admin@acme.example A cégének egyik felhasználója bejelentkezett",
                "north SignalR u:1 UserLogin: User 1 signed in at 2030-01-01 00:00:00Z.",
            ],
            await RoutedAsync());
    }

    [Fact]
    public async Task Handle_CompanyArchived_SendsSignalRToTheCompanyGroupAndEmailToItsAdmin()
    {
        // Arrange
        var archived = new CompanyArchived("north", 200, _now);

        // Act
        await _sut.Handle(archived, new Envelope(), Ct);

        // Assert
        Assert.Equal(
            [
                "north Email admin@globex.example Your company was archived",
                "north SignalR c:200 CompanyArchived: Company 200 was archived at 2030-01-01 00:00:00Z.",
            ],
            await RoutedAsync());
    }

    [Fact]
    public async Task Handle_ProjectCreated_SendsSignalRToTheOwnerAndEmailToTheAdminOfTheCompanyOfTheOwner()
    {
        // Arrange
        var created = new ProjectCreated("north", 7, "Bridge", OwnerUserId: 2);

        // Act
        await _sut.Handle(created, new Envelope(), Ct);

        // Assert
        Assert.Equal(
            [
                "north Email admin@acme.example Új projekt jött létre a cégénél",
                "north SignalR u:2 ProjectCreated: Project Bridge (7) of user 2 was created.",
            ],
            await RoutedAsync());
    }

    [Fact]
    public async Task Handle_ProjectFinished_SendsSignalRToTheOwnerAndEmailToTheAdminOfTheCompanyOfTheOwner()
    {
        // Arrange
        var finished = new ProjectFinished("north", 8, "Tower", OwnerUserId: 3);

        // Act
        await _sut.Handle(finished, new Envelope(), Ct);

        // Assert
        Assert.Equal(
            [
                "north Email admin@globex.example A project of your company is finished",
                "north SignalR u:3 ProjectFinished: Project Tower (8) of user 3 is finished.",
            ],
            await RoutedAsync());
    }

    [Fact]
    public async Task Handle_TenantMaintenance_SendsSignalRToTheGroupAndEmailToTheAdminOfEachCompanyOfTheTenant()
    {
        // Arrange
        var maintenance = new TenantMaintenance("north", _now.AddDays(1), _now.AddDays(1).AddHours(2));

        // Act
        await _sut.Handle(maintenance, new Envelope(), Ct);

        // Assert
        Assert.Equal(
            [
                "north Email admin@acme.example Karbantartás várható a rendszerben",
                "north Email admin@globex.example A maintenance of the system is planned",
                "north SignalR c:100 TenantMaintenance: The maintenance of north runs from 2030-01-02 00:00:00Z to 2030-01-02 02:00:00Z.",
                "north SignalR c:200 TenantMaintenance: The maintenance of north runs from 2030-01-02 00:00:00Z to 2030-01-02 02:00:00Z.",
            ],
            await RoutedAsync());
    }

    [Fact]
    public async Task Handle_UserLoginOfAUserIdThatTwoTenantsHave_SendsOnlyToTheRecipientsOfTheTenantOfTheEvent()
    {
        // Arrange
        var login = new UserLogin("south", 1, _now);

        // Act
        await _sut.Handle(login, new Envelope(), Ct);

        // Assert
        Assert.Equal(
            [
                "south Email admin@initech.example A user of your company signed in",
                "south SignalR u:1 UserLogin: User 1 signed in at 2030-01-01 00:00:00Z.",
            ],
            await RoutedAsync());
    }

    [Fact]
    public async Task Handle_TenantMaintenanceDeliveredTwiceInOneEnvelope_StoresTheMessagesOfEachCompanyOnce()
    {
        // Arrange
        var maintenance = new TenantMaintenance("north", _now.AddDays(1), _now.AddDays(1).AddHours(2));
        var envelope = new Envelope();
        await _sut.Handle(maintenance, envelope, Ct);

        // Act
        await _sut.Handle(maintenance, envelope, Ct);

        // Assert
        Assert.Equal(4, (await RoutedAsync()).Count);
    }

    [Fact]
    public async Task Handle_EventWithAnInvalidTenant_ThrowsSoThatWolverineRetriesTheEnvelope()
    {
        // Arrange
        var login = new UserLogin("", 1, _now); // invalid: an empty tenant id

        // Act
        var accept = () => _sut.Handle(login, new Envelope(), Ct);

        // Assert
        await Assert.ThrowsAsync<InvalidOperationException>(accept);
    }

    /// <summary>
    /// Routes the events of the inbox, and gives the messages that the outbox stores, in ordinal order: an email as
    /// <c>{tenant} Email {to} {subject}</c>, and a SignalR message as <c>{tenant} SignalR {u|c}:{id} {message type}: {text}</c>.
    /// </summary>
    private async Task<IReadOnlyList<string>> RoutedAsync()
    {
        await _host.ProcessInboxAsync();
        List<string> stored = [];
        foreach (var channel in new[] { EmailChannel.Id, SignalRChannel.Id })
        {
            stored.AddRange((await ClaimAsync(channel)).Select(Describe));
        }

        return [.. stored.Order(StringComparer.Ordinal)];
    }

    /// <summary>Routes the events of the inbox, and gives the SignalR messages that the outbox stores.</summary>
    private async Task<IReadOnlyList<OutgoingMessage>> SignalRMessagesAsync()
    {
        await _host.ProcessInboxAsync();
        return await ClaimAsync(SignalRChannel.Id);
    }

    private async Task<IReadOnlyList<OutgoingMessage>> ClaimAsync(MsgHatch.Domain.Identity.ChannelId channel)
    {
        var claim = await _host.Provider.GetRequiredService<IOutboxDispatchStore>()
            .ClaimDueAsync(channel, 1000, _now.UtcDateTime, TimeSpan.FromMinutes(1), Ct);
        return claim.Value.Messages;
    }

    private static string Describe(OutgoingMessage message) => message.Content switch
    {
        EmailMessage email => $"{message.Tenant?.Value} Email {email.To} {email.Subject}",
        SignalREnvelope<Notification> signalR =>
            $"{message.Tenant?.Value} SignalR {TargetOf(message.Target)} {message.Type.Name}: {signalR.Payload.Text}",
        _ => $"{message.Tenant?.Value} {message.Channel.Name} {message.ContentType}",
    };

    private static string TargetOf(DeliveryTarget target) => $"{(target.Kind == DeliveryTargetKind.User ? "u" : "c")}:{target.Id}";
}
