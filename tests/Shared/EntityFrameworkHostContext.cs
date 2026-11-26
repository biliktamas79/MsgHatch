using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using MsgHatch.Data.EFCore;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Inbox;
using MsgHatch.Domain.Management;

namespace MsgHatch.Tests.Shared;

/// <summary>
/// A context of a host with its own table <c>Orders</c>, the inbox of MsgHatch (D331), and a naming convention that writes
/// each column name of the host in lower case, as a naming convention package of a host does.
/// </summary>
/// <param name="options">The options of the context.</param>
internal sealed class TestHostContext(DbContextOptions<TestHostContext> options) : DbContext(options)
{
    public DbSet<TestOrder> Orders => Set<TestOrder>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);
        configurationBuilder.Conventions.Add(static _ => new LowerCaseColumnNames());
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.Entity<TestOrder>(static order =>
        {
            order.ToTable("Orders");
            order.Property(static row => row.Text).HasMaxLength(100);
        });
        modelBuilder.MapMsgHatchInbox(this);
    }

    private sealed class LowerCaseColumnNames : IModelFinalizingConvention
    {
        public void ProcessModelFinalizing(IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context)
        {
            foreach (var property in modelBuilder.Metadata.GetEntityTypes().SelectMany(static entity => entity.GetProperties()))
            {
                property.Builder.HasColumnName(property.Name.ToLowerInvariant());
            }
        }
    }
}

/// <summary>A row of the table of the host.</summary>
internal sealed class TestOrder
{
    public int Id { get; set; }

    public string Text { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }
}

/// <summary>
/// The inbox tenant store of the inbox in the transaction of the host (D332), as a host uses it: each add stages the row
/// in a new context of the host, and saves that context. When the save fails because the id is stored, the add stores
/// nothing and gives no error, as a host does for an event that its broker delivers again. Each member runs on the store
/// of <see cref="EntityFrameworkInboxStore.For(TenantKey, DbContext)"/>, so the inbox contract covers all of them.
/// </summary>
/// <param name="store">The store.</param>
/// <param name="key">The tenant key.</param>
/// <param name="hostContexts">Makes a new context of the host.</param>
internal sealed class HostSavingInboxStore(EntityFrameworkInboxStore store, TenantKey key, Func<DbContext> hostContexts) : IInboxStore
{
    public async ValueTask<Error?> AddAsync(ReceivedMessage message, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(message);
        await using var host = hostContexts();
        if (await store.For(key, host).AddAsync(message, ct) is { } error)
        {
            return error;
        }

        try
        {
            await host.SaveChangesAsync(ct);
            return null;
        }
        catch (DbUpdateException)
        {
            if (!await IsStoredAsync(message, ct))
            {
                throw;
            }

            return null;
        }
    }

    public async ValueTask<ErrorOr<MessagePage<ReceivedMessageSummary>>> QueryAsync(InboxQuery query, CancellationToken ct)
    {
        await using var host = hostContexts();
        return await store.For(key, host).QueryAsync(query, ct);
    }

    public async ValueTask<ErrorOr<ReceivedMessage?>> GetAsync(Guid id, CancellationToken ct)
    {
        await using var host = hostContexts();
        return await store.For(key, host).GetAsync(id, ct);
    }

    public async ValueTask<ErrorOr<IReadOnlyList<MessageCount>>> CountAsync(TrafficQuery query, CancellationToken ct)
    {
        await using var host = hostContexts();
        return await store.For(key, host).CountAsync(query, ct);
    }

    public async ValueTask<ErrorOr<int>> RetryAsync(IReadOnlyList<Guid> ids, DateTime nowUtc, int addedTries, CancellationToken ct)
    {
        await using var host = hostContexts();
        return await store.For(key, host).RetryAsync(ids, nowUtc, addedTries, ct);
    }

    public async ValueTask<ErrorOr<int>> CancelAsync(IReadOnlyList<Guid> ids, DateTime nowUtc, CancellationToken ct)
    {
        await using var host = hostContexts();
        return await store.For(key, host).CancelAsync(ids, nowUtc, ct);
    }

    private async Task<bool> IsStoredAsync(ReceivedMessage message, CancellationToken ct) =>
        (await store.For(key).GetAsync(message.Delivery.Id, ct)).Value is not null;
}
