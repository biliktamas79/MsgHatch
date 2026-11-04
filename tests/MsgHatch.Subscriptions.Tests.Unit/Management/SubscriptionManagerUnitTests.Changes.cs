using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Routing;
using MsgHatch.Email;
using MsgHatch.SignalR;
using MsgHatch.Subscriptions.Management;
using MsgHatch.Subscriptions.Tests.Unit.TestTypes;
using NSubstitute;

namespace MsgHatch.Subscriptions.Tests.Unit.Management;

/// <summary>The changes of the subscription management API: the validation rules 2.3, the conflict rules 2.4, and the examples M10 to M22.</summary>
public sealed partial class SubscriptionManagerUnitTests
{
    private static readonly SubscriptionOwner _acme = SubscriptionOwner.ForCompany(Scenario.Acme);

    public static TheoryData<string, SubscriptionDraft> ValidationRows => new()
    {
        { "2.3.1: a company owner with no company", Draft(SubscriptionOwner.ForCompany(default), "A", EmailChannel.Id, new EmailChannelSettings()) },
        { "2.3.2: a message type that is not registered", Draft(User("1"), "Z", EmailChannel.Id, new EmailChannelSettings()) },
        { "2.3.2: a message type with no scope function", Draft(User("1"), "F", EmailChannel.Id, new EmailChannelSettings()) },
        { "M10 2.3.3: a channel that the message type has not registered", Draft(User("2"), "A", SignalRChannel.Id, new SignalRChannelSettings()) },
        { "2.3.3: an opt-out of a channel that the message type has not registered", Draft(User("1"), "A", SignalRChannel.Id, null, SubscriptionKind.OptOut) },
        { "2.3.3: a channel subscription with no settings", Draft(User("1"), "A", EmailChannel.Id, null) },
        { "2.3.3: a channel subscription with no channel", Draft(User("1"), "A", null, new EmailChannelSettings()) },
        { "M11 2.3.4: the settings of another channel", Draft(User("1"), "C", EmailChannel.Id, new SignalRChannelSettings()) },
        { "M12 2.3.6: a language that is not a known culture", Draft(User("1"), "A", EmailChannel.Id, new EmailChannelSettings("xx-invalid")) },
        { "M13 2.3.8: an opt-out with settings", Draft(User("1"), "A", EmailChannel.Id, new EmailChannelSettings(), SubscriptionKind.OptOut) },
        {
            "M21 2.3.10: a user subscription for the company",
            Draft(User("1"), "A", EmailChannel.Id, new EmailChannelSettings(), recipients: SubscriptionRecipients.Company)
        },
        { "M21 2.3.11: a named user of another company", Draft(_acme, "D", EmailChannel.Id, new EmailChannelSettings(), recipients: NamedUsers("3")) },
        { "2.3.11: no named users", Draft(_acme, "D", EmailChannel.Id, new EmailChannelSettings(), recipients: NamedUsers()) },
        { "2.3.11: a duplicate named user", Draft(_acme, "D", EmailChannel.Id, new EmailChannelSettings(), recipients: NamedUsers("2", "2")) },
        { "2.3.11: a named user that the directory does not know", Draft(_acme, "D", EmailChannel.Id, new EmailChannelSettings(), recipients: NamedUsers("99")) },
        { "M21 2.3.12: an opt-out for the company", Draft(_acme, "A", null, null, SubscriptionKind.OptOut, SubscriptionRecipients.Company) },
        { "2.3.12: a mandatory opt-out", Draft(SubscriptionOwner.Default, "A", EmailChannel.Id, null, SubscriptionKind.OptOut, mandatory: true) },
        { "M21 2.3.13: a mandatory user subscription", Draft(User("1"), "A", EmailChannel.Id, new EmailChannelSettings(), mandatory: true) },
        {
            "2.3.13: a mandatory subscription for named users",
            Draft(_acme, "D", EmailChannel.Id, new EmailChannelSettings(), recipients: NamedUsers("2"), mandatory: true)
        },
    };

    public static TheoryData<string, ChannelSettings, int?> PagerRows => new()
    {
        { "2.3.4: email settings on the channel Pager, whose validator accepts them", new EmailChannelSettings(), 400 },
        { "2.3.5: a language on a channel with no feature Languages", new PagerSettings("en"), 400 },
        { "2.3.7: settings that the validator of the channel rejects", new PagerSettings(Priority: -1), 400 },
        { "valid settings", new PagerSettings(), null },
    };

    public static TheoryData<int, int?> NamedUserCountRows => new()
    {
        { 99, null },
        { 100, null },
        { 101, 400 },
    };

    public static TheoryData<string, Guid, SubscriptionDraft> UpdateValidationRows => new()
    {
        { "M17 2.3.9: #6 with the owner user 2", Scenario.S6.Id, Draft(User("2"), "B", EmailChannel.Id, new EmailChannelSettings()) },
        { "M17 2.3.9: #6 with the message type A", Scenario.S6.Id, Draft(User("1"), "A", EmailChannel.Id, new EmailChannelSettings()) },
        { "2.3.9: #10 with the recipients scope users", Scenario.S10.Id, Draft(_acme, "B", EmailChannel.Id, new EmailChannelSettings()) },
    };

    public static TheoryData<string, SubscriptionDraft> ConflictRows => new()
    {
        { "M15 2.4.3: Acme, B, Email, and the opt-out #4 for all channels", Draft(_acme, "B", EmailChannel.Id, new EmailChannelSettings()) },
        { "M15 2.4.2: user 1, B, an opt-out for all channels, and #6", Draft(User("1"), "B", null, null, SubscriptionKind.OptOut) },
        { "M15 2.4.1: user 1, B, Email, and #6", Draft(User("1"), "B", EmailChannel.Id, new EmailChannelSettings("hu")) },
        { "M20 2.4.5: user 1, C, an opt-out of the mandatory SignalR #3", Draft(User("1"), "C", SignalRChannel.Id, null, SubscriptionKind.OptOut) },
        { "M20 2.4.5: user 1, C, SignalR in hu, and the mandatory #3", Draft(User("1"), "C", SignalRChannel.Id, new SignalRChannelSettings("hu")) },
    };

    public static TheoryData<string, SubscriptionDraft> DirectoryCallRows => new()
    {
        { "the named users of a company subscription", Draft(_acme, "D", EmailChannel.Id, new EmailChannelSettings(), recipients: NamedUsers("2")) },
        { "the company of the owner of a user subscription", Draft(User("1"), "A", EmailChannel.Id, new EmailChannelSettings("hu")) },
    };

    [Theory]
    [MemberData(nameof(ValidationRows))]
    public async Task CreateAsync_InvalidDraft_GivesValidationFailedAndChangesNothing(string row, SubscriptionDraft draft)
    {
        // Arrange
        var manager = Manager;

        // Act
        var created = await manager.CreateAsync(draft, Ct);

        // Assert
        Assert.Equal((row, 400, 12), (row, created.Error?.Type.Code, await StoredCountAsync()));
    }

    [Theory]
    [MemberData(nameof(PagerRows))]
    public async Task CreateAsync_SettingsOfTheTestChannelPager_GivesTheCodeOfTheRule(string row, ChannelSettings settings, int? expected)
    {
        // Arrange
        await using var provider = ManagerHost.Build(_time, withPager: true);
        await using var scope = provider.CreateAsyncScope();

        // Act
        var created = await scope.ServiceProvider.GetRequiredService<ISubscriptionManager>()
            .CreateAsync(Draft(User("1"), "P", ManagerHost.Pager, settings), Ct);

        // Assert
        Assert.Equal((row, expected), (row, created.Error?.Type.Code));
    }

    [Theory]
    [MemberData(nameof(NamedUserCountRows))]
    public async Task CreateAsync_NamedUsersOfTheCompany_AcceptsAtMost100(int count, int? expected)
    {
        // Arrange
        var users = Enumerable.Range(1, 101).Select(static n => new UserProfile(new UserId($"n{n}"), Scenario.Acme, "en", new Dictionary<ChannelId, string>()));
        await using var provider = ManagerHost.Build(_time, ScenarioDoubles.ADirectory([.. users], Scenario.Companies));
        await using var scope = provider.CreateAsyncScope();
        var named = SubscriptionRecipients.ForUsers([.. Enumerable.Range(1, count).Select(static n => new UserId($"n{n}"))]);

        // Act
        var created = await scope.ServiceProvider.GetRequiredService<ISubscriptionManager>()
            .CreateAsync(Draft(_acme, "D", EmailChannel.Id, new EmailChannelSettings(), recipients: named), Ct);

        // Assert
        Assert.Equal((count, expected), (count, created.Error?.Type.Code));
    }

    [Theory]
    [MemberData(nameof(UpdateValidationRows))]
    public async Task UpdateAsync_ChangeOfTheOwnerTheTypeOrTheRecipientKind_GivesValidationFailedAndKeepsTheStoredSubscription(
        string row, Guid id, SubscriptionDraft draft)
    {
        // Arrange
        var stored = (await Store.GetAsync(id, Ct)).Value;

        // Act
        var updated = await Manager.UpdateAsync(id, 1, draft, Ct);

        // Assert
        Assert.Equal((row, 400, stored), (row, updated.Error?.Type.Code, (await Store.GetAsync(id, Ct)).Value));
    }

    [Theory]
    [MemberData(nameof(ConflictRows))]
    public async Task CreateAsync_ConflictingDraft_GivesConflictAndChangesNothing(string row, SubscriptionDraft draft)
    {
        // Arrange
        var manager = Manager;

        // Act
        var created = await manager.CreateAsync(draft, Ct);

        // Assert
        Assert.Equal((row, 409, 12), (row, created.Error?.Type.Code, await StoredCountAsync()));
    }

    [Theory]
    [MemberData(nameof(DirectoryCallRows))]
    public async Task CreateAsync_ErrorOfTheDirectory_GivesThatError(string row, SubscriptionDraft draft)
    {
        // Arrange
        var error = new Error(Errors.Unavailable);
        var directory = Substitute.For<IRecipientDirectory>();
        directory.GetUsersAsync(default!, Ct).ReturnsForAnyArgs(ErrorOr<IReadOnlyList<UserProfile>>.ForError(error));
        await using var provider = ManagerHost.Build(_time, directory);
        await using var scope = provider.CreateAsyncScope();

        // Act
        var created = await scope.ServiceProvider.GetRequiredService<ISubscriptionManager>().CreateAsync(draft, Ct);

        // Assert
        Assert.Equal((row, true), (row, ReferenceEquals(error, created.Error)));
    }

    [Fact]
    public async Task CreateAsync_UserSubscriptionOfAChannelThatTheCompanyMadeMandatory_GivesConflict()
    {
        // Arrange
        Assert.True((await Manager.CreateAsync(Draft(_acme, "A", EmailChannel.Id, new EmailChannelSettings(), mandatory: true), Ct)).IsSuccess);

        // Act
        var created = await Manager.CreateAsync(Draft(User("1"), "A", EmailChannel.Id, new EmailChannelSettings("hu")), Ct);

        // Assert
        Assert.Equal(409, created.Error?.Type.Code);
    }

    [Fact]
    public async Task CreateAsync_InvalidAndConflictingDraft_GivesValidationFailed()
    {
        // Arrange
        var draft = Draft(User("1"), "B", EmailChannel.Id, new EmailChannelSettings("xx-invalid"));

        // Act
        var created = await Manager.CreateAsync(draft, Ct);

        // Assert
        Assert.Equal(400, created.Error?.Type.Code);
    }

    [Fact]
    public async Task CreateAsync_ValidDraft_GivesRevision1AVersion7IdAndTheFakeTime_M14()
    {
        // Arrange
        var draft = Draft(User("2"), "C", EmailChannel.Id, new EmailChannelSettings("hu"));

        // Act
        var created = await Manager.CreateAsync(draft, Ct);

        // Assert
        var subscription = created.Value;
        Assert.Equal(
            (1, 7, TestHost.Start.UtcDateTime, TestHost.Start.UtcDateTime, subscription),
            (subscription.Revision, subscription.Id.Version, subscription.CreatedAtUtc, subscription.LastModifiedAtUtc, (await Store.GetAsync(subscription.Id, Ct)).Value));
    }

    [Fact]
    public async Task CreateAsync_EmailOfUser2ForC_ResolutionAddsTheEmailAndKeepsSignalR_M14()
    {
        // Arrange
        await Manager.CreateAsync(Draft(User("2"), "C", EmailChannel.Id, new EmailChannelSettings("hu")), Ct);

        // Act
        var deliveries = await ResolveAsync("C", "users 1,2");

        // Assert
        Assert.Equal("u:1 / SignalR / en / Default; u:2 / Email / hu / User; u:2 / SignalR / hu / Default", deliveries);
    }

    [Fact]
    public async Task UpdateAsync_ExpectedRevision1_GivesRevision2AndTheNewModificationTime_M16()
    {
        // Arrange
        _time.Advance(TimeSpan.FromHours(1));
        var draft = Draft(User("1"), "B", EmailChannel.Id, new EmailChannelSettings("en", EmailFormat.Html));

        // Act
        var updated = await Manager.UpdateAsync(Scenario.S6.Id, 1, draft, Ct);

        // Assert
        Assert.Equal(
            (2, TestHost.Start.UtcDateTime.AddHours(1), EmailFormat.Html, updated.Value),
            (updated.Value.Revision, updated.Value.LastModifiedAtUtc, ((EmailChannelSettings)updated.Value.Settings!).Format,
                (await Store.GetAsync(Scenario.S6.Id, Ct)).Value));
    }

    [Fact]
    public async Task UpdateAsync_SecondUpdateWithTheOldRevision_GivesConflict_M16()
    {
        // Arrange
        var draft = Draft(User("1"), "B", EmailChannel.Id, new EmailChannelSettings("en", EmailFormat.Html));
        await Manager.UpdateAsync(Scenario.S6.Id, 1, draft, Ct);

        // Act
        var updated = await Manager.UpdateAsync(Scenario.S6.Id, 1, draft, Ct);

        // Assert
        Assert.Equal(409, updated.Error?.Type.Code);
    }

    [Fact]
    public async Task UpdateAsync_UnknownId_GivesNotFound()
    {
        // Arrange
        var draft = Draft(User("1"), "B", EmailChannel.Id, new EmailChannelSettings());

        // Act
        var updated = await Manager.UpdateAsync(Scenario.LabelId(51), 1, draft, Ct);

        // Assert
        Assert.Equal(404, updated.Error?.Type.Code);
    }

    [Fact]
    public async Task DeleteAsync_OptOut7_ReadGivesNotFound_M18()
    {
        // Arrange
        Assert.Null(await Manager.DeleteAsync(Scenario.S7.Id, Ct));

        // Act
        var read = await Manager.GetSubscriptionAsync(Scenario.S7.Id, Ct);

        // Assert
        Assert.Equal(404, read.Error?.Type.Code);
    }

    [Fact]
    public async Task DeleteAsync_OptOut7_ResolutionOfEGivesTheCompanySubscription5_M18()
    {
        // Arrange
        await Manager.DeleteAsync(Scenario.S7.Id, Ct);

        // Act
        var deliveries = await ResolveAsync("E", "users 1");

        // Assert
        Assert.Equal("u:1 / Email / en / Company", deliveries);
    }

    [Fact]
    public async Task DeleteAsync_UnknownId_Succeeds()
    {
        // Arrange
        var id = Scenario.LabelId(52);

        // Act
        var error = await Manager.DeleteAsync(id, Ct);

        // Assert
        Assert.Null(error);
    }

    [Fact]
    public async Task CreateAsync_OptOutOfUser1ForAllChannelsOfC_KeepsTheMandatorySignalR_M20()
    {
        // Arrange
        Assert.True((await Manager.CreateAsync(Draft(User("1"), "C", null, null, SubscriptionKind.OptOut), Ct)).IsSuccess);

        // Act
        var deliveries = await ResolveAsync("C", "users 1,2");

        // Assert
        Assert.Equal("u:1 / SignalR / en / Default; u:2 / SignalR / hu / Default", deliveries);
    }

    [Fact]
    public async Task CreateAsync_OptOutOfUser2ForEmailOfE_OverridesTheCompanySubscription5_M22()
    {
        // Arrange
        Assert.True((await Manager.CreateAsync(Draft(User("2"), "E", EmailChannel.Id, null, SubscriptionKind.OptOut), Ct)).IsSuccess);

        // Act
        var deliveries = await ResolveAsync("E", "users 2");

        // Assert
        Assert.Equal("", deliveries);
    }

    private static SubscriptionDraft Draft(
        SubscriptionOwner owner,
        string type,
        ChannelId? channel,
        ChannelSettings? settings,
        SubscriptionKind kind = SubscriptionKind.Channel,
        SubscriptionRecipients? recipients = null,
        bool mandatory = false) =>
        new(owner, new MessageType(type), kind, channel, settings, recipients, mandatory);

    private static SubscriptionOwner User(string id) => SubscriptionOwner.ForUser(new UserId(id));

    private static SubscriptionRecipients NamedUsers(params string[] users) =>
        SubscriptionRecipients.ForUsers([.. users.Select(static user => new UserId(user))]);
}
