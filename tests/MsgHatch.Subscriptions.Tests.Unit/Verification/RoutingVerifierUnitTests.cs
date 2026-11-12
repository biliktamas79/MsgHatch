using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Outbox;
using MsgHatch.Domain.Routing;
using MsgHatch.Email.DependencyInjection;
using MsgHatch.Subscriptions.DependencyInjection;
using MsgHatch.Subscriptions.Tests.Unit.TestTypes;
using MsgHatch.Subscriptions.Verification;
using NSubstitute;

namespace MsgHatch.Subscriptions.Tests.Unit.Verification;

/// <summary>
/// The verification examples X1 to X7 of <c>docs/subscription-domain.md</c> on the scenario, with substitutes for the store
/// and the directory, the in-memory outbox, and the embedded test templates, and the rules D160 and D161. A row has the
/// form <c>type / test case / owner / target / channel / language / outcome</c>, and a failed row ends with the error code
/// and name.
/// </summary>
public sealed class RoutingVerifierUnitTests
{
    private const string _hungarianOnlyA = "MsgHatch.Subscriptions.Tests.Unit.TemplateFilesHungarianA";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<string, string, string[]> ExampleRows => new()
    {
        {
            "X1", "A",
            ["A / basic / Default / u:1 / Email / en / Passed", "A / basic / Default / u:1 / Email / hu / Passed"]
        },
        {
            "X2", "B",
            [
                "B / basic / User 1 / u:1 / Email / en / Passed", "B / basic / Company Acme / c:100 / Email / hu / Passed",
                "B / basic / User 1 / u:1 / Email / en / Passed", "B / basic / Company Acme / c:100 / Email / hu / Passed",
            ]
        },
        {
            "X3", "E",
            ["E / basic / User 1 / none / none / en / Passed", "E / basic / User 1 / none / none / hu / Passed"]
        },
        {
            "X5", "D",
            [
                "D / basic / Company Acme / u:2 / Email / hu / Passed", "D / basic / Company Acme / u:2 / Email / hu / Passed",
                "D / none / Company Globex / none / SignalR / none / Skipped", "D / none / Company Globex / none / Email / none / Skipped",
                "D / none / User 2 / none / Email / none / Skipped",
            ]
        },
    };

    [Theory]
    [MemberData(nameof(ExampleRows))]
    public async Task VerifyAsync_ScenarioAndOneMessageTypeForTestUser1_GivesTheRowsOfTheExample(string example, string type, string[] expected)
    {
        // Arrange
        await using var provider = AHost();

        // Act
        var report = await VerifyAsync(provider, ["1"], type);

        // Assert
        Assert.StartsWith("X", example, StringComparison.Ordinal);
        Assert.Equal(expected, Rows(report.Value));
    }

    [Fact]
    public async Task VerifyAsync_TemplateOfAOnlyInHungarian_GivesAFailedEnglishRowWithNotFound_X4()
    {
        // Arrange
        await using var provider = AHost(templatePrefix: _hungarianOnlyA);

        // Act
        var report = await VerifyAsync(provider, ["1"], "A");

        // Assert
        Assert.Equal(
            ["A / basic / Default / u:1 / Email / en / Failed 404 NotFound", "A / basic / Default / u:1 / Email / hu / Passed"],
            Rows(report.Value));
    }

    [Fact]
    public async Task VerifyAsync_MessageTypeWithNoTestCase_GivesOneSkippedRowForTheType_X6()
    {
        // Arrange
        await using var provider = AHost(testEventTypes: "ABDE");

        // Act
        var report = await VerifyAsync(provider, ["1"], "C");

        // Assert
        Assert.Equal(["C / none / none / none / none / none / Skipped"], Rows(report.Value));
    }

    [Fact]
    public async Task VerifyAsync_AllTypesForTestUsers1And3_StoresNothingAndFailsNoCheck_X7()
    {
        // Arrange
        await using var provider = AHost();

        // Act
        var report = await VerifyAsync(provider, ["1", "3"], type: null);

        // Assert
        Assert.Equal(
            (0, 0, true),
            (report.Value.Failed, provider.GetRequiredService<InMemoryOutboxStore>().GetMessages().Count, report.Value.Passed > 0));
    }

    [Fact]
    public async Task VerifyAsync_TestUserThatTheDirectoryDoesNotKnow_GivesAFailedRowWithNotFoundForEachLanguage_D160()
    {
        // Arrange
        await using var provider = AHost();

        // Act
        var report = await VerifyAsync(provider, ["99"], "A");

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
        await using var provider = AHost(directory: directory);

        // Act
        var report = await VerifyAsync(provider, ["1"], "B");

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
        await using var provider = AHostWithOnlyA(static m => m
            .WithTestEvent("basic", static user => new EventA([user.Value]))
            .WithPayload<NoticeEmail, FailingNoticeEmailLoader<EventA>>(static p => p.AsEmail("AEmail")));

        // Act
        var report = await VerifyAsync(provider, ["1"], "A", ["en"]);

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
        await using var provider = AHost(directory: directory);

        // Act
        var report = await VerifyAsync(provider, ["1"], "B");

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
        await using var provider = AHost(directory: directory);

        // Act
        var report = await VerifyAsync(provider, ["1"], "A");

        // Assert
        Assert.Equal(Errors.Unavailable.Code, report.Error?.Type.Code);
    }

    [Fact]
    public async Task VerifyAsync_TestEventWithACompanyScope_GivesTheTestProfileAsTheProfileOfTheTestUserInTheCompany()
    {
        // Arrange
        await using var provider = AHostWithOnlyA(static m => m
            .WithTestEvent("company", static _ => new EventA(Company: "100"))
            .WithPayload<NoticeEmail, NoticeEmailLoader<EventA>>(static p => p.AsEmail("AEmail")));

        // Act
        var report = await VerifyAsync(provider, ["1"], "A", ["hu"]);

        // Assert
        Assert.Equal(
            [
                "A / company / Default / u:1 / Email / hu / Passed", "A / company / Default / u:2 / Email / hu / Passed",
                $"A / company / Default / u:5 / Email / hu / Failed {Errors.ValidationFailed.Code} ValidationFailed",
            ],
            Rows(report.Value));
    }

    private static ServiceProvider AHost(
        string testEventTypes = "ABCDE", string templatePrefix = SubscriptionHost.TemplatePrefix, IRecipientDirectory? directory = null)
    {
        var services = TestHost.CreateServices();
        services.AddScenarioHost(
            static _ => ScenarioDoubles.AScenarioStore(), _ => directory ?? ScenarioDoubles.AScenarioDirectory(), testEventTypes, templatePrefix);
        return services.Build();
    }

    private static ServiceProvider AHostWithOnlyA(Func<MessageBuilder<EventA>, MessageBuilder<EventA>> testCaseAndPayload)
    {
        var services = TestHost.CreateServices();
        services.AddMsgHatch(static o => o.Roles = MsgHatchRoles.Consumer)
            .UseSubscriptions()
            .UseInMemoryStores()
            .UseSubscriptionStore(static (_, _) => ScenarioDoubles.AScenarioStore())
            .UseRecipientDirectory(static (_, _) => ScenarioDoubles.AScenarioDirectory())
            .UseEmbeddedTemplates(typeof(SubscriptionHost).Assembly, SubscriptionHost.TemplatePrefix)
            .AddMessage("A", 1, SubscriptionTestJson.Default.EventA, m =>
                testCaseAndPayload(m.WithScope(static e => ScenarioEvents.ScopeOf(e.Users, e.Company))));
        return services.Build();
    }

    private static async Task<ErrorOr<RoutingVerificationReport>> VerifyAsync(
        IServiceProvider provider, string[] testUsers, string? type, string[]? languages = null)
    {
        await using var scope = provider.CreateAsyncScope();
        var request = new RoutingVerificationRequest(
            [.. testUsers.Select(static user => new UserId(user))], languages ?? ["en", "hu"], type is null ? null : new MessageType(type));
        return await scope.ServiceProvider.GetRequiredService<IRoutingVerifier>().VerifyAsync(request, Ct);
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
