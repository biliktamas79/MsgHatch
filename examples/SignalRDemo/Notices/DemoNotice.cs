using MsgHatch.Domain.Routing;

namespace SignalRDemo.Notices;

/// <summary>
/// The domain event of a notification of the publish panel: its target and its text. The tenant comes from the message
/// context. The message type name is also the name of the client method of the notification.
/// </summary>
/// <param name="Target">The target: a tenant, a company, or a user of the tenant.</param>
/// <param name="Text">The text.</param>
public sealed record DemoNotice(DeliveryTarget Target, string Text)
{
    /// <summary>The message type name, and the name of the client method.</summary>
    public const string TypeName = nameof(DemoNotice);
}

/// <summary>The payload that a session gets: the id of the sent notification, its tenant, its target, and its text.</summary>
/// <param name="Id">The id of the notification, which the publish endpoint gives.</param>
/// <param name="Tenant">The tenant of the notification.</param>
/// <param name="Level">The kind of the target: <c>Tenant</c>, <c>Company</c>, or <c>User</c>.</param>
/// <param name="TargetId">The company or the user, or empty for a tenant target.</param>
/// <param name="Text">The text.</param>
public sealed record DemoPayload(Guid Id, string? Tenant, string Level, string TargetId, string Text);
