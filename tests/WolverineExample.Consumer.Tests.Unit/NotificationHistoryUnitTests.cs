using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Management;
using MsgHatch.Domain.Routing;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using MsgHatch.SignalR;
using Wolverine;
using WolverineExample.Consumer.Notifications;
using WolverineExample.Consumer.Tests.Unit.TestTypes;
using WolverineExample.Contracts;
using static WolverineExample.Consumer.Tests.Unit.TestTypes.ConsumerTestHost;

namespace WolverineExample.Consumer.Tests.Unit;

/// <summary>
/// The catch-up of the SignalR notifications of the user <c>north:1</c> of the company Acme <c>100</c>. The tests accept
/// events at different times of the fake clock, and read the history from the outbox of the consumer host.
/// </summary>
public sealed class NotificationHistoryUnitTests : IAsyncLifetime
{
    private static readonly TenantId _north = new("north");
    private static readonly UserId _user1 = UserId.From(1);
    private static readonly CompanyId _acme = CompanyId.From(100);
    private ConsumerTestHost _host = null!;
    private NotificationHistory _sut = null!;

    public async ValueTask InitializeAsync()
    {
        _host = await ConsumerTestHost.StartAsync();
        _sut = _host.Provider.GetRequiredService<NotificationHistory>();
    }

    public ValueTask DisposeAsync() => _host.DisposeAsync();

    [Fact]
    public async Task GetMissedAsync_NoLastMessage_GivesTheNotificationsOfTheUserAndOfItsCompanyOldestFirst()
    {
        // Arrange
        await AcceptAsync(new UserLogin("north", 1, Now));
        await AcceptAsync(new CompanyArchived("north", 200, Now), after: TimeSpan.FromMinutes(1));
        await AcceptAsync(new ProjectCreated("north", 7, "Bridge", OwnerUserId: 2), after: TimeSpan.FromMinutes(1));
        await AcceptAsync(new TenantMaintenance("north", Now, Now.AddHours(1)), after: TimeSpan.FromMinutes(1));

        // Act
        var missed = await _sut.GetMissedAsync(_north, _user1, _acme, lastMessageId: null, Ct);

        // Assert
        Assert.Equal([nameof(UserLogin), nameof(TenantMaintenance)], TypesOf(missed));
    }

    [Fact]
    public async Task GetMissedAsync_NoLastMessageAndANotificationOlderThanFiveMinutes_LeavesOutTheOlderNotification()
    {
        // Arrange
        await AcceptAsync(new UserLogin("north", 1, Now));
        await AcceptAsync(new ProjectCreated("north", 7, "Bridge", OwnerUserId: 1), after: TimeSpan.FromMinutes(6));

        // Act
        var missed = await _sut.GetMissedAsync(_north, _user1, _acme, lastMessageId: null, Ct);

        // Assert
        Assert.Equal([nameof(ProjectCreated)], TypesOf(missed));
    }

    [Fact]
    public async Task GetMissedAsync_LastMessageOfTheUser_GivesTheNotificationsFromFiveMinutesBeforeItWithNoLastMessage()
    {
        // Arrange
        await AcceptAsync(new UserLogin("north", 1, Now));
        await AcceptAsync(new ProjectCreated("north", 7, "Bridge", OwnerUserId: 1), after: TimeSpan.FromMinutes(7));
        await AcceptAsync(new ProjectFinished("north", 7, "Bridge", OwnerUserId: 1), after: TimeSpan.FromMinutes(3));
        await AcceptAsync(new TenantMaintenance("north", Now, Now.AddHours(1)), after: TimeSpan.FromMinutes(10));
        var last = await SignalRIdAsync(nameof(ProjectFinished), User(_user1));

        // Act
        var missed = await _sut.GetMissedAsync(_north, _user1, _acme, last, Ct);

        // Assert
        Assert.Equal([nameof(ProjectCreated), nameof(TenantMaintenance)], TypesOf(missed));
    }

    [Fact]
    public async Task GetMissedAsync_LastMessageOfAnotherUser_GivesTheNotificationsOfTheLastFiveMinutes()
    {
        // Arrange
        await AcceptAsync(new UserLogin("north", 1, Now));
        await AcceptAsync(new ProjectCreated("north", 7, "Bridge", OwnerUserId: 2), after: TimeSpan.FromMinutes(2));
        await AcceptAsync(new ProjectFinished("north", 7, "Bridge", OwnerUserId: 1), after: TimeSpan.FromMinutes(9));
        var foreign = await SignalRIdAsync(nameof(ProjectCreated), User(UserId.From(2))); // not a notification of user 1

        // Act
        var missed = await _sut.GetMissedAsync(_north, _user1, _acme, foreign, Ct);

        // Assert
        Assert.Equal([nameof(ProjectFinished)], TypesOf(missed));
    }

    [Fact]
    public async Task GetMissedAsync_UnknownLastMessage_GivesTheNotificationsOfTheLastFiveMinutes()
    {
        // Arrange
        await AcceptAsync(new UserLogin("north", 1, Now));
        await AcceptAsync(new ProjectFinished("north", 7, "Bridge", OwnerUserId: 1), after: TimeSpan.FromMinutes(6));
        var unknown = Guid.Parse("00000000-0000-0000-0000-000000000001"); // no outgoing message has this id

        // Act
        var missed = await _sut.GetMissedAsync(_north, _user1, _acme, unknown, Ct);

        // Assert
        Assert.Equal([nameof(ProjectFinished)], TypesOf(missed));
    }

    [Fact]
    public async Task GetMissedAsync_CancelledNotification_LeavesItOut()
    {
        // Arrange
        await AcceptAsync(new UserLogin("north", 1, Now));
        await AcceptAsync(new ProjectCreated("north", 7, "Bridge", OwnerUserId: 1), after: TimeSpan.FromMinutes(1));
        var cancelled = await SignalRIdAsync(nameof(ProjectCreated), User(_user1));
        Assert.False((await Manager().CancelOutgoingAsync(new OutboxQuery(Ids: [cancelled]), Ct)).IsError);

        // Act
        var missed = await _sut.GetMissedAsync(_north, _user1, _acme, lastMessageId: null, Ct);

        // Assert
        Assert.Equal([nameof(UserLogin)], TypesOf(missed));
    }

    [Fact]
    public async Task GetMissedAsync_Notification_GivesItsIdTextAndTime()
    {
        // Arrange
        await AcceptAsync(new ProjectCreated("north", 7, "Bridge", OwnerUserId: 1));
        var id = await SignalRIdAsync(nameof(ProjectCreated), User(_user1));

        // Act
        var missed = await _sut.GetMissedAsync(_north, _user1, _acme, lastMessageId: null, Ct);

        // Assert
        Assert.Equal(
            new MissedNotification(nameof(ProjectCreated), id, "Project Bridge (7) of user 1 was created.", TestHost.Start.UtcDateTime),
            Assert.Single(missed.Value));
    }

    private DateTimeOffset Now => _host.Time.GetUtcNow();

    /// <summary>Moves the clock forward, accepts an event, and routes it, so the outbox stores its messages at that time.</summary>
    private async Task AcceptAsync<TEvent>(TEvent domainEvent, TimeSpan after = default)
    {
        _host.Time.Advance(after);
        await HandleAsync(domainEvent);
        await _host.ProcessInboxAsync();
    }

    private Task HandleAsync<TEvent>(TEvent domainEvent) => domainEvent switch
    {
        UserLogin e => _host.Receiver.Handle(e, new Envelope(), Ct),
        CompanyArchived e => _host.Receiver.Handle(e, new Envelope(), Ct),
        ProjectCreated e => _host.Receiver.Handle(e, new Envelope(), Ct),
        ProjectFinished e => _host.Receiver.Handle(e, new Envelope(), Ct),
        TenantMaintenance e => _host.Receiver.Handle(e, new Envelope(), Ct),
        _ => throw new ArgumentException($"No handler for {typeof(TEvent).Name}.", nameof(domainEvent)),
    };

    private async Task<Guid> SignalRIdAsync(string messageType, DeliveryTarget target)
    {
        var page = await Manager().GetOutgoingMessagesAsync(
            new OutboxQuery(Type: new MessageType(messageType), Channel: SignalRChannel.Id, Target: target), Ct);
        return Assert.Single(page.Value.Items).Delivery.Id;
    }

    private IDeliveryManager Manager() => _host.Provider.GetRequiredKeyedService<IDeliveryManager>(TenantKey.For(_north));

    private static DeliveryTarget User(UserId user) => new(DeliveryTargetKind.User, user.Value);

    private static string[] TypesOf(MsgHatch.Domain.Failures.ErrorOr<IReadOnlyList<MissedNotification>> missed) =>
        [.. missed.Value.Select(static notification => notification.MessageType)];
}
