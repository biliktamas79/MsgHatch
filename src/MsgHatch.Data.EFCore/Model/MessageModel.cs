using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MsgHatch.Data.EFCore.Messages;

namespace MsgHatch.Data.EFCore.Model;

/// <summary>
/// The mapping of the two message tables (section 4.5). <see cref="MsgHatchDbContext"/> and a context of the host with
/// <see cref="MsgHatchModelBuilderExtensions.MapMsgHatchInbox"/> map the inbox with the same code, so both contexts have
/// the same inbox table (D331).
/// </summary>
internal static class MessageModel
{
    public const string InboxTable = "Inbox";

    /// <summary>The inbox table with its unique key of the tenant and the id, and its due index.</summary>
    /// <param name="inbox">The inbox entity type.</param>
    /// <param name="jsonColumnType">The JSON type of the options or the dialect, or null for the text type of the provider.</param>
    public static void Inbox(EntityTypeBuilder<InboxRow> inbox, string? jsonColumnType)
    {
        Message(inbox, InboxTable, jsonColumnType);
        inbox.HasIndex(static row => new { row.TenantId, row.Id }).IsUnique().HasDatabaseName("UX_Inbox_Key");
        inbox.HasIndex(static row => new { row.Status, row.NextTryAtUtc, row.Sequence }).HasDatabaseName("IX_Inbox_Due");
    }

    /// <summary>The table name, the key, the column rules, and the indexes that the two message tables share.</summary>
    /// <param name="message">The entity type of a message table.</param>
    /// <param name="table">The table name with no prefix.</param>
    /// <param name="jsonColumnType">The JSON type of the options or the dialect, or null for the text type of the provider.</param>
    public static void Message<TRow>(EntityTypeBuilder<TRow> message, string table, string? jsonColumnType)
        where TRow : MessageRow
    {
        message.ToTable(table);
        message.HasKey(static row => row.Sequence);
        message.Property(static row => row.TenantId).IsId();
        message.Property(static row => row.Id).IsGuid();
        message.Property(static row => row.Status).IsEnum();
        message.Property(static row => row.ClaimToken).IsGuid();
        message.Property(static row => row.ContentType).IsName();
        message.Property(static row => row.Flags).IsEnum();
        message.Property(static row => row.Content).IsJson(jsonColumnType);
        message.HasIndex(static row => row.Flags).HasDatabaseName($"IX_{table}_Flags");
        message.HasIndex(static row => new { row.TenantId, row.CreatedAtUtc, row.Id }).HasDatabaseName($"IX_{table}_Created");
    }
}
