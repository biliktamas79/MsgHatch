using Microsoft.EntityFrameworkCore;
using MsgHatch.Data.EFCore;

namespace MsgHatch.Subscriptions.Data.EFCore;

/// <summary>
/// The EF Core model of the subscriptions (section 4.5, D120): the table <c>MsgHatchSubscription</c> in the schema of
/// <see cref="EntityFrameworkStoreOptions.Schema"/>, with the column rules of the core EF Core stores (D178). The unique
/// index of the owner stops a second subscription with the same owner, message type, channel, and recipient kind, and
/// also serves <c>FindForDeliveryAsync</c>. The package creates no schema: the host creates it with its own EF Core
/// migrations of this context.
/// </summary>
public sealed class MsgHatchSubscriptionDbContext : DbContext
{
    private readonly string _schema;

    private readonly string? _textCollation;

    /// <summary>Makes the context. <c>UseEntityFrameworkSubscriptionStore</c> registers a pooled factory of it.</summary>
    /// <param name="options">The options of the context.</param>
    public MsgHatchSubscriptionDbContext(DbContextOptions<MsgHatchSubscriptionDbContext> options)
        : base(options)
    {
        var storeOptions = StoreOptionsExtension.Of(options);
        _schema = storeOptions.Schema;
        _textCollation = storeOptions.TextCollation;
    }

    internal DbSet<SubscriptionRow> Subscriptions => Set<SubscriptionRow>();

    /// <inheritdoc />
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        StoreModel.ConfigureConventions(configurationBuilder, _textCollation);

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.HasDefaultSchema(_schema);
        modelBuilder.Entity<SubscriptionRow>(static subscription =>
        {
            subscription.ToTable("MsgHatchSubscription");
            subscription.HasKey(static row => row.Id);
            subscription.Property(static row => row.Id).IsGuid();
            subscription.Property(static row => row.TenantId).IsId();
            subscription.Property(static row => row.Level).IsEnum();
            subscription.Property(static row => row.CompanyId).IsId();
            subscription.Property(static row => row.UserId).IsId();
            subscription.Property(static row => row.MessageType).IsName();
            subscription.Property(static row => row.Kind).IsEnum();
            subscription.Property(static row => row.Channel).IsName();
            subscription.Property(static row => row.SettingsType).IsName();
            subscription.Property(static row => row.Recipients).IsEnum();
            subscription.HasIndex(static row => new { row.TenantId, row.MessageType, row.Level, row.CompanyId, row.UserId, row.Channel, row.Recipients })
                .IsUnique()
                .HasDatabaseName("UX_MsgHatchSubscription_Owner");
        });
    }
}
