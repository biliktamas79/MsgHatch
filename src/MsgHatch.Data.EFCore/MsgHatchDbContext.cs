using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MsgHatch.Data.EFCore;

/// <summary>
/// The EF Core model of the inbox, the outbox, the down states, and the registry settings (section 4.5, D120), in the
/// schema of <see cref="EntityFrameworkStoreOptions.Schema"/>. The table and column names are the names of section 4.5
/// (D56), with the same indexes. The partial claim index of each message table is a filtered index with
/// <c>WHERE Status = 'Pending'</c> (D104). The package creates no schema: the host creates it with its own EF Core
/// migrations of this context.
/// </summary>
public sealed class MsgHatchDbContext : DbContext
{
    private const string _pending = "Status = 'Pending'";

    private readonly string _schema;

    /// <summary>Makes the context. <c>UseEntityFrameworkStores</c> registers a pooled factory of it.</summary>
    /// <param name="options">The options of the context.</param>
    public MsgHatchDbContext(DbContextOptions<MsgHatchDbContext> options)
        : base(options)
    {
        _schema = SchemaOptionsExtension.SchemaOf(options);
    }

    internal DbSet<InboxRow> Inbox => Set<InboxRow>();

    internal DbSet<OutboxRow> Outbox => Set<OutboxRow>();

    internal DbSet<ChannelDownRow> ChannelDowns => Set<ChannelDownRow>();

    internal DbSet<ChannelEntryRow> ChannelEntries => Set<ChannelEntryRow>();

    internal DbSet<MessageTypeEntryRow> MessageTypeEntries => Set<MessageTypeEntryRow>();

    /// <inheritdoc />
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        StoreModel.ConfigureConventions(configurationBuilder, Database.ProviderName);

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.HasDefaultSchema(_schema);
        modelBuilder.Entity<InboxRow>(static inbox =>
        {
            Message(inbox, "MsgHatchInbox");
            inbox.HasIndex(static row => new { row.TenantId, row.Id }).IsUnique().HasDatabaseName("UX_MsgHatchInbox_Key");
            inbox.HasIndex(static row => row.NextTryAtUtc).HasFilter(_pending).HasDatabaseName("IX_MsgHatchInbox_Due");
        });
        modelBuilder.Entity<OutboxRow>(static outbox =>
        {
            Message(outbox, "MsgHatchOutbox");
            outbox.Property(static row => row.SourceMessageId).IsGuid();
            outbox.Property(static row => row.MessageType).IsName();
            outbox.Property(static row => row.Channel).IsName();
            outbox.Property(static row => row.TargetKind).IsEnum();
            outbox.Property(static row => row.TargetId).IsId();
            outbox.Property(static row => row.TraceType).IsName();
            outbox.HasIndex(static row => row.Id).IsUnique().HasDatabaseName("UX_MsgHatchOutbox_Id");
            outbox.HasIndex(static row => new { row.Channel, row.NextTryAtUtc }).HasFilter(_pending).HasDatabaseName("IX_MsgHatchOutbox_Due");
            outbox.HasIndex(static row => new { row.TenantId, row.TargetKind, row.TargetId, row.CreatedAtUtc })
                .HasDatabaseName("IX_MsgHatchOutbox_Target");
            outbox.HasIndex(static row => row.SourceMessageId).HasDatabaseName("IX_MsgHatchOutbox_Source");
        });
        modelBuilder.Entity<ChannelDownRow>(static down =>
        {
            down.ToTable("MsgHatchChannelDown");
            down.HasKey(static row => row.Channel);
            down.Property(static row => row.Channel).IsName();
            down.Property(static row => row.Reason).IsEnum();
            down.Property(static row => row.ErrorName).IsName();
            down.Property(static row => row.Note).HasMaxLength(500);
        });
        modelBuilder.Entity<ChannelEntryRow>(static entry =>
        {
            entry.ToTable("MsgHatchChannelEntry");
            entry.HasKey(static row => row.Channel);
            entry.Property(static row => row.Channel).IsName();
            entry.Property(static row => row.DisplayName).HasMaxLength(200);
            entry.Property(static row => row.Description).HasMaxLength(2000);
        });
        modelBuilder.Entity<MessageTypeEntryRow>(static entry =>
        {
            entry.ToTable("MsgHatchMessageTypeEntry");
            entry.HasKey(static row => row.Type);
            entry.Property(static row => row.Type).IsName();
            entry.Property(static row => row.DisplayName).HasMaxLength(200);
            entry.Property(static row => row.Description).HasMaxLength(2000);
        });
    }

    private static void Message<TRow>(EntityTypeBuilder<TRow> message, string table)
        where TRow : MessageRow
    {
        message.ToTable(table);
        message.HasKey(static row => row.Sequence);
        message.Property(static row => row.TenantId).IsId();
        message.Property(static row => row.Id).IsGuid();
        message.Property(static row => row.Status).IsEnum();
        message.Property(static row => row.ContentType).IsName();
        message.Property(static row => row.Flags).IsEnum();
        message.HasIndex(static row => row.Flags).HasDatabaseName($"IX_{table}_Flags");
        message.HasIndex(static row => new { row.TenantId, row.CreatedAtUtc, row.Id }).HasDatabaseName($"IX_{table}_Created");
    }
}
