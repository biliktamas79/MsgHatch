using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using MsgHatch.Data.EFCore;
using MsgHatch.Data.EFCore.Model;

namespace MsgHatch.Subscriptions.Data.EFCore;

/// <summary>
/// The EF Core model of the subscriptions (section 4.5, D120): the table <c>Subscription</c> in the schema of
/// <see cref="EntityFrameworkStoreOptions.Schema"/>, with no prefix <c>MsgHatch</c> (D321), or <c>MsgHatchSubscription</c>
/// with the dialect of a database with no schemas (D326), and with the column rules of the core EF Core stores (D178). The
/// unique index of the owner on the hash <c>OwnerKey</c> stops a second subscription with the same owner, message type,
/// channel, and recipient kind (D327). The index of the tenant and the message type serves <c>FindForDeliveryAsync</c>.
/// Only <c>MigrateMsgHatchAsync</c> creates the schema, with the migrations of a provider package or of the host (D320).
/// </summary>
public sealed class MsgHatchSubscriptionDbContext : DbContext
{
    private readonly StoreOptionsExtension _storeOptions;

    /// <summary>Makes the context. <c>UseEntityFrameworkSubscriptionStore</c> registers a pooled factory of it.</summary>
    /// <param name="options">The options of the context.</param>
    public MsgHatchSubscriptionDbContext(DbContextOptions<MsgHatchSubscriptionDbContext> options)
        : base(options) =>
        _storeOptions = StoreOptionsExtension.Of(options);

    internal DbSet<SubscriptionRow> Subscriptions => Set<SubscriptionRow>();

    /// <inheritdoc />
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        StoreModel.ConfigureConventions(configurationBuilder, _storeOptions.TextCollation);

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        var jsonColumnType = _storeOptions.JsonColumnType;
        modelBuilder.Entity<SubscriptionRow>(subscription =>
        {
            subscription.ToTable("Subscription");
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
            subscription.Property(static row => row.Settings).IsJson(jsonColumnType);
            subscription.Property(static row => row.NamedUsers).IsJson(jsonColumnType);
            subscription.Property(static row => row.Recipients).IsEnum();
            subscription.Property(static row => row.OwnerKey).HasMaxLength(SHA256.HashSizeInBytes).IsFixedLength();
            subscription.HasIndex(static row => row.OwnerKey).IsUnique().HasDatabaseName("UX_Subscription_Owner");
            subscription.HasIndex(static row => new { row.TenantId, row.MessageType }).HasDatabaseName("IX_Subscription_Type");
        });
        StoreModel.PlaceTables(modelBuilder, _storeOptions);
    }
}
