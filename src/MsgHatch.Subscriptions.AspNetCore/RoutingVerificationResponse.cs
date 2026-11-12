using System.Text.Json;
using MsgHatch.Domain.Persistence;
using MsgHatch.Domain.Routing;
using MsgHatch.Subscriptions.Verification;

namespace MsgHatch.Subscriptions.AspNetCore;

/// <summary>The body of the response of <c>POST {prefix}/verification</c>: the report of a routing verification (section 4.18).</summary>
/// <param name="Checks">The rows.</param>
/// <param name="Passed">The number of passed rows.</param>
/// <param name="Failed">The number of failed rows. A test job can fail when it is greater than 0.</param>
/// <param name="Skipped">The number of skipped rows.</param>
public sealed record RoutingVerificationResponse(IReadOnlyList<RoutingCheckResponse> Checks, int Passed, int Failed, int Skipped)
{
    internal static RoutingVerificationResponse From(RoutingVerificationReport report, IContentTypeRegistry registry) =>
        new([.. report.Checks.Select(check => RoutingCheckResponse.From(check, registry))], report.Passed, report.Failed, report.Skipped);
}

/// <summary>One row of a routing verification report, with the owner fields of <see cref="SubscriptionResponse"/>.</summary>
/// <param name="MessageType">The message type.</param>
/// <param name="TestCase">The test case, or null for a skipped row.</param>
/// <param name="Level">The level of the owner, or null for no owner.</param>
/// <param name="CompanyId">The company of a company owner, else null.</param>
/// <param name="UserId">The user of a user owner, else null.</param>
/// <param name="TargetKind">The kind of the target of the route, or null when the row has no route.</param>
/// <param name="TargetId">The id of the target of the route, or null when the row has no route.</param>
/// <param name="Channel">The channel, or null.</param>
/// <param name="Language">The language, or null for a skipped row.</param>
/// <param name="Outcome">The outcome.</param>
/// <param name="ErrorCode">The error code of a failed row, else null.</param>
/// <param name="ErrorName">The error name of a failed row, else null.</param>
/// <param name="Trace">The resolution trace as the JSON of the trace type <c>Trace:Subscription</c>, or null.</param>
public sealed record RoutingCheckResponse(
    string MessageType,
    string? TestCase,
    SubscriptionLevel? Level,
    string? CompanyId,
    string? UserId,
    DeliveryTargetKind? TargetKind,
    string? TargetId,
    string? Channel,
    string? Language,
    RoutingCheckOutcome Outcome,
    int? ErrorCode,
    string? ErrorName,
    JsonElement? Trace)
{
    internal static RoutingCheckResponse From(RoutingCheck check, IContentTypeRegistry registry) =>
        new(
            check.Type.Name, check.TestCase, check.Owner?.Level, check.Owner?.Company?.Value, check.Owner?.User?.Value, check.Target?.Kind,
            check.Target?.Id, check.Channel?.Name, check.Language, check.Outcome, check.ErrorCode, check.ErrorName,
            check.Trace is null ? null : SettingsJson.Write(registry, check.Trace));
}
