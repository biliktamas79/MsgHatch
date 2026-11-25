namespace MsgHatch.Subscriptions.Data.EFCore;

/// <summary>
/// A row of the table <c>Subscription</c>: one subscription with its fields as columns, and its channel
/// settings as JSON with their content type and version (D149). A missing tenant, company, user, or channel is an empty
/// text, as in the SQLite store. <see cref="OwnerKey"/> is the hash of the key, so the unique index of the owner has the
/// same size on each provider (D327).
/// </summary>
internal sealed class SubscriptionRow
{
    public string Id { get; set; } = string.Empty;

    public int Revision { get; set; }

    public string TenantId { get; set; } = string.Empty;

    public string Level { get; set; } = string.Empty;

    public string CompanyId { get; set; } = string.Empty;

    public string UserId { get; set; } = string.Empty;

    public string MessageType { get; set; } = string.Empty;

    public string Kind { get; set; } = string.Empty;

    public string Channel { get; set; } = string.Empty;

    public string? SettingsType { get; set; }

    public int? SettingsVersion { get; set; }

    public string? Settings { get; set; }

    public string Recipients { get; set; } = string.Empty;

    /// <summary>The SHA-256 of <see cref="TenantId"/>, <see cref="MessageType"/>, <see cref="Level"/>, <see cref="CompanyId"/>, <see cref="UserId"/>, <see cref="Channel"/>, and <see cref="Recipients"/>.</summary>
    public byte[] OwnerKey { get; set; } = [];

    public string? NamedUsers { get; set; }

    public bool Mandatory { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime LastModifiedAtUtc { get; set; }
}
