using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MsgHatch.Data.EFCore.Messages;
using MsgHatch.Data.EFCore.Model;
using MsgHatch.Data.EFCore.Registry;

namespace MsgHatch.Data.EFCore;

/// <summary>
/// The EF Core model of the inbox, the outbox, the channel states, and the registry settings (section 4.5, D120), in the
/// schema of <see cref="EntityFrameworkStoreOptions.Schema"/>. The tables and the indexes have the names of the SQLite
/// stores with no prefix <c>MsgHatch</c>, for example <c>Outbox</c>, because the schema keeps them apart from the tables
/// of the host (D56, D321). The due index of each message table has no filter, so each provider can use it:
/// <c>Status</c> is a column of the index, and <c>Sequence</c> is its last column, so the index gives the claim order
/// (D104, D144, D318). The package creates no schema: the host creates it with its own EF Core migrations of this context.
/// </summary>
public sealed class MsgHatchDbContext : DbContext
{
    private readonly string _schema;

    private readonly string? _textCollation;

    private readonly string? _jsonColumnType;

    /// <summary>Makes the context. <c>UseEntityFrameworkStores</c> registers a pooled factory of it.</summary>
    /// <param name="options">The options of the context.</param>
    public MsgHatchDbContext(DbContextOptions<MsgHatchDbContext> options)
        : base(options)
    {
        var storeOptions = StoreOptionsExtension.Of(options);
        _schema = storeOptions.Schema;
        _textCollation = storeOptions.TextCollation;
        _jsonColumnType = storeOptions.JsonColumnType;
    }

    internal DbSet<InboxRow> Inbox => Set<InboxRow>();

    internal DbSet<OutboxRow> Outbox => Set<OutboxRow>();

    internal DbSet<ChannelStateRow> ChannelStates => Set<ChannelStateRow>();

    internal DbSet<ChannelEntryRow> ChannelEntries => Set<ChannelEntryRow>();

    internal DbSet<MessageTypeEntryRow> MessageTypeEntries => Set<MessageTypeEntryRow>();

    /// <inheritdoc />
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        StoreModel.ConfigureConventions(configurationBuilder, _textCollation);

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.HasDefaultSchema(_schema);
        var jsonColumnType = _jsonColumnType;
        modelBuilder.Entity<InboxRow>(inbox =>
        {
            Message(inbox, "Inbox", jsonColumnType);
            inbox.HasIndex(static row => new { row.TenantId, row.Id }).IsUnique().HasDatabaseName("UX_Inbox_Key");
            inbox.HasIndex(static row => new { row.Status, row.NextTryAtUtc, row.Sequence }).HasDatabaseName("IX_Inbox_Due");
        });
        modelBuilder.Entity<OutboxRow>(outbox =>
        {
            Message(outbox, "Outbox", jsonColumnType);
            outbox.Property(static row => row.SourceMessageId).IsGuid();
            outbox.Property(static row => row.MessageType).IsName();
            outbox.Property(static row => row.Channel).IsName();
            outbox.Property(static row => row.TargetKind).IsEnum();
            outbox.Property(static row => row.TargetId).IsId();
            outbox.Property(static row => row.TraceType).IsName();
            outbox.Property(static row => row.Trace).IsJson(jsonColumnType);
            outbox.Property(static row => row.Tries).IsJson(jsonColumnType);
            outbox.HasIndex(static row => row.Id).IsUnique().HasDatabaseName("UX_Outbox_Id");
            outbox.HasIndex(static row => new { row.Channel, row.Status, row.NextTryAtUtc, row.Sequence }).HasDatabaseName("IX_Outbox_Due");
            outbox.HasIndex(static row => new { row.TenantId, row.TargetKind, row.TargetId, row.CreatedAtUtc })
                .HasDatabaseName("IX_Outbox_Target");
            outbox.HasIndex(static row => row.SourceMessageId).HasDatabaseName("IX_Outbox_Source");
        });
        modelBuilder.Entity<ChannelStateRow>(static state =>
        {
            state.ToTable("ChannelState");
            state.HasKey(static row => new { row.Channel, row.Version });
            state.Property(static row => row.Channel).IsName();
            state.Property(static row => row.Kind).IsEnum();
            state.Property(static row => row.PreviousKind).HasMaxLength(32).IsUnicode(false);
            state.Property(static row => row.Reason).HasMaxLength(32).IsUnicode(false);
            state.Property(static row => row.ErrorName).IsName();
            state.Property(static row => row.Note).HasMaxLength(500);
        });
        modelBuilder.Entity<ChannelEntryRow>(static entry =>
        {
            entry.ToTable("ChannelEntry");
            entry.HasKey(static row => row.Channel);
            entry.Property(static row => row.Channel).IsName();
            entry.Property(static row => row.DisplayName).HasMaxLength(200);
            entry.Property(static row => row.Description).HasMaxLength(2000);
        });
        modelBuilder.Entity<MessageTypeEntryRow>(entry =>
        {
            entry.ToTable("MessageTypeEntry");
            entry.HasKey(static row => row.Type);
            entry.Property(static row => row.Type).IsName();
            entry.Property(static row => row.DisabledChannels).IsJson(jsonColumnType);
            entry.Property(static row => row.DisplayName).HasMaxLength(200);
            entry.Property(static row => row.Description).HasMaxLength(2000);
        });
    }

    private static void Message<TRow>(EntityTypeBuilder<TRow> message, string table, string? jsonColumnType)
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
