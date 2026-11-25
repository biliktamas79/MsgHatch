using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Templates.DependencyInjection;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Outbox;
using MsgHatch.Domain.Routing;
using MsgHatch.Channels.Email.DependencyInjection;
using MsgHatch.Subscriptions.DependencyInjection;
using MsgHatch.Subscriptions.Tests.Unit.TestTypes;
using MsgHatch.Subscriptions.Verification;
using NSubstitute;

namespace MsgHatch.Subscriptions.Tests.Unit.Contracts;

/// <summary>
/// The verification examples X1 to X7 of <c>docs/subscription-domain.md</c> on the scenario, with substitutes for the store
/// and the directory, the in-memory outbox, and the embedded test templates, and the rules D160 and D161. A row has the
/// form <c>type / test case / owner / target / channel / language / outcome</c>, and a failed row ends with the error code
/// and name. Each implementation of <see cref="IRoutingVerifier"/> gets them with one subclass that only builds it (D231).
/// </summary>
/// <typeparam name="T">The implementation.</typeparam>
public abstract class RoutingVerifierTestsBase<T>
    where T : class, IRoutingVerifier
{
    private const string _hungarianOnlyA = "MsgHatch.Subscriptions.Tests.Unit.TemplateFilesHungarianA";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [MemberData(nameof(RoutingVerifierTestRows.ExampleRows), MemberType = typeof(RoutingVerifierTestRows))]
    public async Task VerifyAsync_ScenarioAndOneMessageTypeForTestUser1_GivesTheRowsOfTheExample(string example, string type, string[] expected)
    {
        // Arrange
        await using var host = await AHostAsync();

        // Act
        var report = await VerifyAsync(host, ["1"], type);

        // Assert
        Assert.StartsWith("X", example, StringComparison.Ordinal);
        Assert.Equal(expected, Rows(report.Value));
    }

    [Fact]
    public async Task VerifyAsync_TemplateOfAOnlyInHungarian_GivesAFailedEnglishRowWithNotFound_X4()
    {
        // Arrange
        await using var host = await AHostAsync(templatePrefix: _hungarianOnlyA);

        // Act
        var report = await VerifyAsync(host, ["1"], "A");

        // Assert
        Assert.Equal(
            ["A / basic / Default / u:1 / Email / en / Failed 404 NotFound", "A / basic / Default / u:1 / Email / hu / Passed"],
            Rows(report.Value));
    }

    [Fact]
    public async Task VerifyAsync_MessageTypeWithNoTestCase_GivesOneSkippedRowForTheType_X6()
    {
        // Arrange
        await using var host = await AHostAsync(testEventTypes: "ABDE");

        // Act
        var report = await VerifyAsync(host, ["1"], "C");

        // Assert
        Assert.Equal(["C / none / none / none / none / none / Skipped"], Rows(report.Value));
    }

    [Fact]
    public async Task VerifyAsync_AllTypesForTestUsers1And3_StoresNothingAndFailsNoCheck_X7()
    {
        // Arrange
        await using var host = await AHostAsync();

        // Act
        var report = await VerifyAsync(host, ["1", "3"], type: null);

        // Assert
        Assert.Equal(
            (0, 0, true),
            (report.Value.Failed, host.Server.GetRequiredService<InMemoryOutboxStore>().GetMessages().Count, report.Value.Passed > 0));
    }

    [Fact]
    public async Task VerifyAsync_TestUserThatTheDirectoryDoesNotKnow_GivesAFailedRowWithNotFoundForEachLanguage_D160()
    {
        // Arrange
        await using var host = await AHostAsync();

        // Act
        var report = await VerifyAsync(host, ["99"], "A");

        // Assert
        Assert.Equal(
            [
                "A / basic / none / none / none / en / Failed 404 NotFound", "A / basic / none / none / none / hu / Failed 404 NotFound",
                "A / none / Default / none / Email / none / Skipped",
            ],
            Rows(report.Value));
    }

    [Fact]
    public async Task VerifyAsync_DirectoryErrorInTheRouting_GivesAFailedRowForEachVariationAndContinues_D161()
    {
        // Arrange
        var directory = ScenarioDoubles.AScenarioDirectory();
        directory.GetCompanyAsync(default, Ct).ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<CompanyProfile?>.ForError(new Error(Errors.Unavailable))));
        await using var host = await AHostAsync(directory: directory);

        // Act
        var report = await VerifyAsync(host, ["1"], "B");

        // Assert
        var unavailable = $"Failed {Errors.Unavailable.Code} Unavailable";
        Assert.Equal(
            [
                $"B / basic / none / none / none / en / {unavailable}", $"B / basic / none / none / none / hu / {unavailable}",
                "B / none / Default / none / Email / none / Skipped", "B / none / Company Acme / none / none / none / Skipped",
                "B / none / User 1 / none / Email / none / Skipped", "B / none / Company Acme / none / Email / none / Skipped",
            ],
            Rows(report.Value));
    }

    [Fact]
    public async Task VerifyAsync_ErrorOfTheWholePreview_GivesAFailedRowWithThatError_D161()
    {
        // Arrange
        await using var host = await AHostWithOnlyAAsync(static m => m
            .WithTestEvent("basic", static user => new EventA([user.Value]))
            .WithPayload<NoticeEmail, FailingNoticeEmailLoader<EventA>>(static p => p.AsEmail("AEmail")));

        // Act
        var report = await VerifyAsync(host, ["1"], "A", ["en"]);

        // Assert
        Assert.Equal(
            [$"A / basic / none / none / none / en / Failed {Errors.Unavailable.Code} Unavailable", "A / none / Default / none / Email / none / Skipped"],
            Rows(report.Value));
    }

    [Fact]
    public async Task VerifyAsync_CancellationInTheRouting_ReturnsOperationCancelled_D161()
    {
        // Arrange
        var directory = ScenarioDoubles.AScenarioDirectory();
        directory.GetCompanyAsync(default, Ct)
            .ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<CompanyProfile?>.ForError(new Error(Errors.OperationCancelled))));
        await using var host = await AHostAsync(directory: directory);

        // Act
        var report = await VerifyAsync(host, ["1"], "B");

        // Assert
        Assert.Equal(Errors.OperationCancelled.Code, report.Error?.Type.Code);
    }

    [Fact]
    public async Task VerifyAsync_DirectoryErrorForTheTestUsers_ReturnsThatError()
    {
        // Arrange
        var directory = Substitute.For<IRecipientDirectory>();
        directory.GetUsersAsync(default!, Ct)
            .ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<IReadOnlyList<UserProfile>>.ForError(new Error(Errors.Unavailable))));
        await using var host = await AHostAsync(directory: directory);

        // Act
        var report = await VerifyAsync(host, ["1"], "A");

        // Assert
        Assert.Equal(Errors.Unavailable.Code, report.Error?.Type.Code);
    }

    [Fact]
    public async Task VerifyAsync_TestEventWithACompanyScope_GivesTheTestProfileAsTheProfileOfTheTestUserInTheCompany()
    {
        // Arrange
        await using var host = await AHostWithOnlyAAsync(static m => m
            .WithTestEvent("company", static _ => new EventA(Company: "100"))
            .WithPayload<NoticeEmail, NoticeEmailLoader<EventA>>(static p => p.AsEmail("AEmail")));

        // Act
        var report = await VerifyAsync(host, ["1"], "A", ["hu"]);

        // Assert
        Assert.Equal(
            [
                "A / company / Default / u:1 / Email / hu / Passed", "A / company / Default / u:2 / Email / hu / Passed",
                $"A / company / Default / u:5 / Email / hu / Failed {Errors.ValidationFailed.Code} ValidationFailed",
            ],
            Rows(report.Value));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task VerifyAsync_EmptyLanguage_GivesTheErrorOfTheRuleAndReadsNoStore_D248(string language)
    {
        // Arrange
        var store = Substitute.For<ISubscriptionStore>();
        await using var host = await StartAsync(services => services.AddTestServices().AddScenarioHost(
            _ => store, static _ => ScenarioDoubles.AScenarioDirectory(), "ABCDE", SubscriptionHost.TemplatePrefix));
        var request = new RoutingVerificationRequest([new UserId("1")], ["en", language]);

        // Act
        var report = await host.Service(TenantKey.None).VerifyAsync(request, Ct);

        // Assert
        Assert.Equal((RoutingVerificationRules.Check(request), 0), (report.Error, store.ReceivedCalls().Count()));
    }

    [Fact]
    public async Task VerifyAsync_NullRequest_ThrowsArgumentNullException()
    {
        // Arrange
        await using var host = await AHostAsync();

        // Act
        var exception = await Record.ExceptionAsync(async () => await host.Service(TenantKey.None).VerifyAsync(null!, Ct));

        // Assert
        Assert.IsType<ArgumentNullException>(exception);
    }

    /// <summary>Starts a host of the implementation on the services of a test.</summary>
    /// <param name="addServices">Adds the services of the host with the store, the directory, the templates, and the outbox.</param>
    /// <returns>The started host.</returns>
    protected abstract ValueTask<IManagementTestHost<T>> StartAsync(Action<IServiceCollection> addServices);

    private ValueTask<IManagementTestHost<T>> AHostAsync(
        string testEventTypes = "ABCDE", string templatePrefix = SubscriptionHost.TemplatePrefix, IRecipientDirectory? directory = null) =>
        StartAsync(services => services.AddTestServices().AddScenarioHost(
            static _ => ScenarioDoubles.AScenarioStore(), _ => directory ?? ScenarioDoubles.AScenarioDirectory(), testEventTypes, templatePrefix));

    private ValueTask<IManagementTestHost<T>> AHostWithOnlyAAsync(Func<MessageBuilder<EventA>, MessageBuilder<EventA>> testCaseAndPayload) =>
        StartAsync(services => services.AddTestServices().AddMsgHatch(static o => o.Roles = MsgHatchRoles.Consumer)
            .UseSubscriptions()
            .UseInMemoryStores()
            .UseSubscriptionStore(static (_, _) => ScenarioDoubles.AScenarioStore())
            .UseRecipientDirectory(static (_, _) => ScenarioDoubles.AScenarioDirectory())
            .UseEmbeddedTemplates(typeof(SubscriptionHost).Assembly, SubscriptionHost.TemplatePrefix)
            .AddMessage("A", 1, SubscriptionTestJson.Default.EventA, m =>
                testCaseAndPayload(m.WithScope(static e => ScenarioEvents.ScopeOf(e.Users, e.Company)))));

    private static async Task<ErrorOr<RoutingVerificationReport>> VerifyAsync(
        IManagementTestHost<T> host, string[] testUsers, string? type, string[]? languages = null)
    {
        var request = new RoutingVerificationRequest(
            [.. testUsers.Select(static user => new UserId(user))], languages ?? ["en", "hu"], type is null ? null : new MessageType(type));
        return await host.Service(TenantKey.None).VerifyAsync(request, Ct);
    }

    private static string[] Rows(RoutingVerificationReport report) => [.. report.Checks.Select(Describe)];

    private static string Describe(RoutingCheck check) =>
        $"{check.Type.Name} / {check.TestCase ?? "none"} / {OwnerOf(check.Owner)} / {TargetOf(check.Target)} / {check.Channel?.Name ?? "none"} / "
        + $"{check.Language ?? "none"} / {check.Outcome}" + (check.ErrorCode is { } code ? $" {code} {check.ErrorName}" : string.Empty);

    private static string OwnerOf(SubscriptionOwner? owner) =>
        owner is not { } known ? "none"
        : known.Company is { } company ? (company == Scenario.Acme ? "Company Acme" : "Company Globex")
        : known.User is { } user ? $"User {user.Value}"
        : "Default";

    private static string TargetOf(DeliveryTarget? target) =>
        target is { } known ? $"{(known.Kind == DeliveryTargetKind.User ? "u" : "c")}:{known.Id}" : "none";
}
