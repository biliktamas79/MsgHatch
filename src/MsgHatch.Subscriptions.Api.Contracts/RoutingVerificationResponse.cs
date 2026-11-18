using System.Text.Json;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Persistence;
using MsgHatch.Domain.Routing;
using MsgHatch.Subscriptions.Resolution;
using MsgHatch.Subscriptions.Verification;

namespace MsgHatch.Subscriptions.Api.Contracts;

/// <summary>The body of the response of <c>POST {prefix}/verification</c>: the report of a routing verification (section 4.18).</summary>
/// <param name="Checks">The rows.</param>
/// <param name="Passed">The number of passed rows.</param>
/// <param name="Failed">The number of failed rows. A test job can fail when it is greater than 0.</param>
/// <param name="Skipped">The number of skipped rows.</param>
public sealed record RoutingVerificationResponse(IReadOnlyList<RoutingCheckResponse> Checks, int Passed, int Failed, int Skipped)
{
    /// <summary>Gives the body of a report (D228).</summary>
    /// <param name="report">The report.</param>
    /// <param name="registry">The content type registry, which gives the JSON of the trace type.</param>
    /// <returns>The body.</returns>
    public static RoutingVerificationResponse From(RoutingVerificationReport report, IContentTypeRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(registry);
        return new RoutingVerificationResponse(
            [.. report.Checks.Select(check => RoutingCheckResponse.From(check, registry))], report.Passed, report.Failed, report.Skipped);
    }

    /// <summary>
    /// Gives the report of the body, with its rows. The counts come from the rows. A value that is not valid gives
    /// <see cref="Errors.SerializationFailed"/> (D228).
    /// </summary>
    /// <param name="registry">The content type registry, which gives the trace type <c>Trace:Subscription</c>.</param>
    /// <returns>The report, or an error.</returns>
    public ErrorOr<RoutingVerificationReport> ToReport(IContentTypeRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        var checks = new RoutingCheck[(Checks ?? []).Count];
        for (var index = 0; index < checks.Length; index++)
        {
            var check = Checks![index].ToCheck(registry);
            if (check.IsError)
            {
                return check.Error;
            }

            checks[index] = check.Value;
        }

        return new RoutingVerificationReport(checks);
    }
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
    private const string _traceContentType = "Trace:Subscription";

    /// <summary>Gives the body of one row (D228).</summary>
    /// <param name="check">The row.</param>
    /// <param name="registry">The content type registry, which gives the JSON of the trace type.</param>
    /// <returns>The body.</returns>
    public static RoutingCheckResponse From(RoutingCheck check, IContentTypeRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(check);
        ArgumentNullException.ThrowIfNull(registry);
        return new RoutingCheckResponse(
            check.Type.Name, check.TestCase, check.Owner?.Level, check.Owner?.Company?.Value, check.Owner?.User?.Value, check.Target?.Kind,
            check.Target?.Id, check.Channel?.Name, check.Language, check.Outcome, check.ErrorCode, check.ErrorName,
            check.Trace is null ? null : SettingsJson.Write(registry, check.Trace));
    }

    /// <summary>
    /// Gives the row of the body. The trace needs the trace type <c>Trace:Subscription</c> of <c>UseSubscriptions</c> in the
    /// registry of the host, and a host with no such entry gets the row with no trace. A value that is not valid gives
    /// <see cref="Errors.SerializationFailed"/> (D228).
    /// </summary>
    /// <param name="registry">The content type registry, which gives the trace type.</param>
    /// <returns>The row, or an error.</returns>
    public ErrorOr<RoutingCheck> ToCheck(IContentTypeRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        var type = WireValues.MessageTypeOf(MessageType);
        var owner = OwnerOf();
        var target = TargetOf();
        var channel = WireValues.OptionalChannelOf(Channel);
        var outcome = WireValues.EnumOf(Outcome, "outcome");
        var trace = TraceOf(registry);
        var error = type.Error ?? owner.Error ?? target.Error ?? channel.Error ?? outcome.Error ?? trace.Error;
        return error is not null
            ? error
            : new RoutingCheck(type.Value, TestCase, owner.Value, target.Value, channel.Value, Language, Outcome, ErrorCode, ErrorName, trace.Value);
    }

    private ErrorOr<SubscriptionOwner?> OwnerOf()
    {
        if (Level is not { } level)
        {
            return CompanyId is null && UserId is null ? (SubscriptionOwner?)null : WireValues.Invalid("An owner id needs the level of the owner.");
        }

        var owner = WireValues.OwnerOf(level, CompanyId, UserId);
        return owner.IsError ? owner.Error : owner.Value;
    }

    private ErrorOr<DeliveryTarget?> TargetOf() =>
        (TargetKind, TargetId) switch
        {
            (null, null) => (DeliveryTarget?)null,
            ({ } kind, { Length: > 0 } id) when Enum.IsDefined(kind) => new DeliveryTarget(kind, id),
            _ => WireValues.Invalid("A target has a known kind and an id, or the row has neither."),
        };

    private ErrorOr<ResolutionTrace?> TraceOf(IContentTypeRegistry registry)
    {
        if (Trace is not { } json
            || registry.Entries.Where(static entry => entry.ContentType == _traceContentType).MaxBy(static entry => entry.Version) is not { } newest)
        {
            return (ResolutionTrace?)null;
        }

        try
        {
            return json.Deserialize(newest.ContentJsonTypeInfo) is ResolutionTrace trace ? trace : WireValues.Invalid("The trace is not a resolution trace.");
        }
        catch (JsonException exception)
        {
            return WireValues.Invalid(exception.Message);
        }
    }
}
