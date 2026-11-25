using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using MsgHatch.Channels.Email;
using MsgHatch.Channels.SignalR;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Routing;
using MsgHatch.Subscriptions.Management;
using MsgHatch.Subscriptions.Resolution;
using MsgHatch.Subscriptions.Tests.Unit.TestTypes;
using NSubstitute;
using static MsgHatch.Subscriptions.Tests.Unit.Contracts.SubscriptionCommandsTestRows;

namespace MsgHatch.Subscriptions.Tests.Unit.Contracts;

/// <summary>
/// The use cases of <see cref="ISubscriptionCommands"/>: the validation rules 2.3, the conflict rules 2.4, and the
/// examples M10 to M22 of <c>docs/subscription-domain.md</c>, on the in-memory store with the shared scenario and a
/// directory substitute (section 4.17, D231). Each implementation gets them with one subclass that only builds it.
/// </summary>
/// <typeparam name="T">The implementation.</typeparam>
public abstract class SubscriptionCommandsTestsBase<T> : IAsyncLifetime
    where T : class, ISubscriptionCommands
{

    private IManagementTestHost<T> _host = null!;

    protected FakeTimeProvider Time { get; } = TestHost.NewFakeTime();

    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    protected T Commands => _host.Service(TenantKey.None);

    protected ISubscriptionStore Store => _host.Server.GetRequiredService<ISubscriptionStore>();

    public async ValueTask InitializeAsync()
    {
        _host = await StartAsync(ManagerHost.Services(Time));
        await _host.Server.SeedScenarioAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _host.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    [Theory]
    [MemberData(nameof(SubscriptionCommandsTestRows.ValidationRows), MemberType = typeof(SubscriptionCommandsTestRows))]
    public async Task CreateAsync_InvalidDraft_GivesValidationFailedAndChangesNothing(string row, SubscriptionDraft draft)
    {
        // Arrange
        var commands = Commands;

        // Act
        var created = await commands.CreateAsync(draft, Ct);

        // Assert
        Assert.Equal((row, 400, 12), (row, created.Error?.Type.Code, await StoredCountAsync()));
    }

    [Theory]
    [MemberData(nameof(SubscriptionCommandsTestRows.PagerRows), MemberType = typeof(SubscriptionCommandsTestRows))]
    public async Task CreateAsync_SettingsOfTheTestChannelPager_GivesTheCodeOfTheRule(string row, ChannelSettings settings, int? expected)
    {
        // Arrange
        await using var host = await StartAsync(ManagerHost.Services(Time, withPager: true));

        // Act
        var created = await host.Service(TenantKey.None)
            .CreateAsync(Draft(User("1"), "P", ManagerHost.Pager, settings), Ct);

        // Assert
        Assert.Equal((row, expected), (row, created.Error?.Type.Code));
    }

    [Theory]
    [MemberData(nameof(SubscriptionCommandsTestRows.NamedUserCountRows), MemberType = typeof(SubscriptionCommandsTestRows))]
    public async Task CreateAsync_NamedUsersOfTheCompany_AcceptsAtMost100(int count, int? expected)
    {
        // Arrange
        var users = Enumerable.Range(1, 101).Select(static n => new UserProfile(new UserId($"n{n}"), Scenario.Acme, "en", new Dictionary<ChannelId, string>()));
        await using var host = await StartAsync(ManagerHost.Services(Time, ScenarioDoubles.ADirectory([.. users], Scenario.Companies)));
        var named = SubscriptionRecipients.ForUsers([.. Enumerable.Range(1, count).Select(static n => new UserId($"n{n}"))]);

        // Act
        var created = await host.Service(TenantKey.None)
            .CreateAsync(Draft(AcmeOwner, "D", EmailChannel.Id, new EmailChannelSettings(), recipients: named), Ct);

        // Assert
        Assert.Equal((count, expected), (count, created.Error?.Type.Code));
    }

    [Theory]
    [MemberData(nameof(SubscriptionCommandsTestRows.UpdateValidationRows), MemberType = typeof(SubscriptionCommandsTestRows))]
    public async Task UpdateAsync_ChangeOfTheOwnerTheTypeOrTheRecipientKind_GivesValidationFailedAndKeepsTheStoredSubscription(
        string row, Guid id, SubscriptionDraft draft)
    {
        // Arrange
        var stored = (await Store.GetAsync(id, Ct)).Value;

        // Act
        var updated = await Commands.UpdateAsync(id, 1, draft, Ct);

        // Assert
        Assert.Equal((row, 400, stored), (row, updated.Error?.Type.Code, (await Store.GetAsync(id, Ct)).Value));
    }

    [Theory]
    [MemberData(nameof(SubscriptionCommandsTestRows.ConflictRows), MemberType = typeof(SubscriptionCommandsTestRows))]
    public async Task CreateAsync_ConflictingDraft_GivesConflictAndChangesNothing(string row, SubscriptionDraft draft)
    {
        // Arrange
        var commands = Commands;

        // Act
        var created = await commands.CreateAsync(draft, Ct);

        // Assert
        Assert.Equal((row, 409, 12), (row, created.Error?.Type.Code, await StoredCountAsync()));
    }

    [Theory]
    [MemberData(nameof(SubscriptionCommandsTestRows.DirectoryCallRows), MemberType = typeof(SubscriptionCommandsTestRows))]
    public async Task CreateAsync_ErrorOfTheDirectory_GivesThatError(string row, SubscriptionDraft draft)
    {
        // Arrange
        var error = new Error(Errors.Unavailable);
        var directory = Substitute.For<IRecipientDirectory>();
        directory.GetUsersAsync(default!, Ct).ReturnsForAnyArgs(ErrorOr<IReadOnlyList<UserProfile>>.ForError(error));
        await using var host = await StartAsync(ManagerHost.Services(Time, directory));

        // Act
        var created = await host.Service(TenantKey.None).CreateAsync(draft, Ct);

        // Assert
        Assert.Equal((row, error.Type.Code), (row, created.Error?.Type.Code));
    }

    [Fact]
    public async Task CreateAsync_UserSubscriptionOfAChannelThatTheCompanyMadeMandatory_GivesConflict()
    {
        // Arrange
        Assert.True((await Commands.CreateAsync(Draft(AcmeOwner, "A", EmailChannel.Id, new EmailChannelSettings(), mandatory: true), Ct)).IsSuccess);

        // Act
        var created = await Commands.CreateAsync(Draft(User("1"), "A", EmailChannel.Id, new EmailChannelSettings("hu")), Ct);

        // Assert
        Assert.Equal(409, created.Error?.Type.Code);
    }

    [Fact]
    public async Task CreateAsync_InvalidAndConflictingDraft_GivesValidationFailed()
    {
        // Arrange
        var draft = Draft(User("1"), "B", EmailChannel.Id, new EmailChannelSettings("xx-invalid"));

        // Act
        var created = await Commands.CreateAsync(draft, Ct);

        // Assert
        Assert.Equal(400, created.Error?.Type.Code);
    }

    [Fact]
    public async Task CreateAsync_ValidDraft_GivesRevision1AVersion7IdAndTheFakeTime_M14()
    {
        // Arrange
        var draft = Draft(User("2"), "C", EmailChannel.Id, new EmailChannelSettings("hu"));

        // Act
        var created = await Commands.CreateAsync(draft, Ct);

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
        await Commands.CreateAsync(Draft(User("2"), "C", EmailChannel.Id, new EmailChannelSettings("hu")), Ct);

        // Act
        var deliveries = await ResolveAsync("C", "users 1,2");

        // Assert
        Assert.Equal("u:1 / SignalR / en / Default; u:2 / Email / hu / User; u:2 / SignalR / hu / Default", deliveries);
    }

    [Fact]
    public async Task UpdateAsync_ExpectedRevision1_GivesRevision2AndTheNewModificationTime_M16()
    {
        // Arrange
        Time.Advance(TimeSpan.FromHours(1));
        var draft = Draft(User("1"), "B", EmailChannel.Id, new EmailChannelSettings("en", EmailFormat.Html));

        // Act
        var updated = await Commands.UpdateAsync(Scenario.S6.Id, 1, draft, Ct);

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
        await Commands.UpdateAsync(Scenario.S6.Id, 1, draft, Ct);

        // Act
        var updated = await Commands.UpdateAsync(Scenario.S6.Id, 1, draft, Ct);

        // Assert
        Assert.Equal(409, updated.Error?.Type.Code);
    }

    [Fact]
    public async Task UpdateAsync_UnknownId_GivesNotFound()
    {
        // Arrange
        var draft = Draft(User("1"), "B", EmailChannel.Id, new EmailChannelSettings());

        // Act
        var updated = await Commands.UpdateAsync(Scenario.LabelId(51), 1, draft, Ct);

        // Assert
        Assert.Equal(404, updated.Error?.Type.Code);
    }

    [Fact]
    public async Task DeleteAsync_OptOut7_StoreGivesNoSubscription_M18()
    {
        // Arrange
        Assert.Null(await Commands.DeleteAsync(Scenario.S7.Id, Ct));

        // Act
        var read = await Store.GetAsync(Scenario.S7.Id, Ct);

        // Assert
        Assert.Null(read.Value);
    }

    [Fact]
    public async Task DeleteAsync_OptOut7_ResolutionOfEGivesTheCompanySubscription5_M18()
    {
        // Arrange
        await Commands.DeleteAsync(Scenario.S7.Id, Ct);

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
        var error = await Commands.DeleteAsync(id, Ct);

        // Assert
        Assert.Null(error);
    }

    [Fact]
    public async Task CreateAsync_OptOutOfUser1ForAllChannelsOfC_KeepsTheMandatorySignalR_M20()
    {
        // Arrange
        Assert.True((await Commands.CreateAsync(Draft(User("1"), "C", null, null, SubscriptionKind.OptOut), Ct)).IsSuccess);

        // Act
        var deliveries = await ResolveAsync("C", "users 1,2");

        // Assert
        Assert.Equal("u:1 / SignalR / en / Default; u:2 / SignalR / hu / Default", deliveries);
    }

    [Fact]
    public async Task CreateAsync_OptOutOfUser2ForEmailOfE_OverridesTheCompanySubscription5_M22()
    {
        // Arrange
        Assert.True((await Commands.CreateAsync(Draft(User("2"), "E", EmailChannel.Id, null, SubscriptionKind.OptOut), Ct)).IsSuccess);

        // Act
        var deliveries = await ResolveAsync("E", "users 2");

        // Assert
        Assert.Equal("", deliveries);
    }

    [Theory]
    [MemberData(nameof(SubscriptionCommandsTestRows.RuleAndStateRows), MemberType = typeof(SubscriptionCommandsTestRows))]
    public async Task CreateAsync_DraftThatBreaksARuleAndAStateCheck_GivesTheErrorOfTheRuleAndChangesNothing(string row, SubscriptionDraft draft)
    {
        // Arrange
        var rule = SubscriptionDraftRules.Check(draft);

        // Act
        var created = await Commands.CreateAsync(draft, Ct);

        // Assert
        Assert.Equal((row, true, rule, 12), (row, rule is not null, created.Error, await StoredCountAsync()));
    }

    [Fact]
    public async Task UpdateAsync_UnknownIdAndADraftThatBreaksARule_GivesTheErrorOfTheRule()
    {
        // Arrange
        var draft = Draft(User("1"), "B", EmailChannel.Id, null);

        // Act
        var updated = await Commands.UpdateAsync(Scenario.LabelId(53), 1, draft, Ct);

        // Assert
        Assert.Equal(SubscriptionDraftRules.Check(draft), updated.Error);
    }

    [Fact]
    public async Task CreateAndUpdateAsync_NullDraft_ThrowArgumentNullException()
    {
        // Act
        var create = await Record.ExceptionAsync(async () => await Commands.CreateAsync(null!, Ct));
        var update = await Record.ExceptionAsync(async () => await Commands.UpdateAsync(Scenario.S6.Id, 1, null!, Ct));

        // Assert
        Assert.Equal((typeof(ArgumentNullException), typeof(ArgumentNullException)), (create?.GetType(), update?.GetType()));
    }

    /// <summary>Starts a host of the implementation on the services of a test.</summary>
    /// <param name="addServices">Adds the services of the host with the store, the directory, and the registry.</param>
    /// <returns>The started host.</returns>
    protected abstract ValueTask<IManagementTestHost<T>> StartAsync(Action<IServiceCollection> addServices);

    private async Task<string> ResolveAsync(string type, string scope)
    {
        var subscriptions = await Store.QueryAsync(new SubscriptionQuery(new MessageType(type)), Ct);
        var resolution = SubscriptionResolver.Resolve(
            Scenario.ChannelsOf(type), Scenario.ScopeOf(scope), subscriptions.Value, Scenario.Users, Scenario.Companies, "en");
        return string.Join("; ", resolution.Deliveries.Select(Scenario.Describe).Order(StringComparer.Ordinal));
    }

    private async Task<int> StoredCountAsync() => (await Store.QueryAsync(new SubscriptionQuery(), Ct)).Value.Count;
}
