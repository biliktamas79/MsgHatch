using Microsoft.Extensions.Options;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Pipeline;
using MsgHatch.Domain.Routing;
using SignalRDemo.Tenants;

namespace SignalRDemo.Notices;

/// <summary>
/// The publish endpoint of the dashboard. It gives each notification to <see cref="INotificationService"/>, so the
/// router of the host and the outbox send it, not the hub. It answers with the id of the notification and the panes
/// that must get it.
/// </summary>
public static class DemoPublisher
{
    /// <summary>The route of the publish endpoint.</summary>
    public const string Route = "/demo/publish";

    internal static async Task<IResult> HandleAsync(
        PublishRequest request, IOptions<DemoDataOptions> data, INotificationService notifications, CancellationToken ct)
    {
        var target = TargetOf(data.Value, request);
        if (target.IsError)
        {
            return DemoProblems.Of(target.Error);
        }

        var id = Guid.NewGuid();
        var (tenant, deliveryTarget) = target.Value;
        var result = await notifications.ProcessAsync(new DemoNotice(deliveryTarget, request.Text ?? string.Empty), new MessageContext(id, tenant), ct);
        return result.IsError
            ? DemoProblems.Of(result.Error)
            : Results.Ok(new PublishResponse(id, ExpectedPanes.Of(tenant.Value, deliveryTarget, request.Panes ?? [])));
    }

    private static ErrorOr<(TenantId Tenant, DeliveryTarget Target)> TargetOf(DemoDataOptions data, PublishRequest request)
    {
        var tenant = data.FindTenant(request.Tenant);
        var known = request.Level switch
        {
            DeliveryTargetKind.Tenant => tenant is not null && string.IsNullOrEmpty(request.TargetId),
            DeliveryTargetKind.Company => request.TargetId is { } company && tenant?.Companies.Contains(company) == true,
            DeliveryTargetKind.User => tenant?.FindUser(request.TargetId) is not null,
            _ => false,
        };
        return known
            ? (new TenantId(tenant!.Id), new DeliveryTarget(request.Level, request.TargetId ?? string.Empty))
            : new Error(Errors.NotFound, "The tenant has no such target.");
    }
}

/// <summary>A notification of the publish panel, with the state of each pane.</summary>
/// <param name="Tenant">The tenant.</param>
/// <param name="Level">The kind of the target.</param>
/// <param name="TargetId">The company or the user, or null for a tenant target.</param>
/// <param name="Text">The text.</param>
/// <param name="Panes">The state of each pane of the dashboard.</param>
public sealed record PublishRequest(
    string? Tenant, DeliveryTargetKind Level, string? TargetId, string? Text, IReadOnlyList<PaneState>? Panes);

/// <summary>The id of a sent notification, and the panes that must get it.</summary>
/// <param name="Id">The id of the notification, which each payload of it has.</param>
/// <param name="Expected">The ids of the expected panes.</param>
public sealed record PublishResponse(Guid Id, IReadOnlyList<string> Expected);
