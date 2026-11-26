using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace MsgHatch.Data.EFCore.Tests.Unit.TestTypes;

/// <summary>
/// A context of a host with its own table <c>Orders</c>, the inbox of MsgHatch (D331), and a naming convention that writes
/// each column name of the host in lower case, as a naming convention package of a host does.
/// </summary>
/// <param name="options">The options of the context.</param>
public sealed class TestHostContext(DbContextOptions<TestHostContext> options) : DbContext(options)
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
public sealed class TestOrder
{
    public int Id { get; set; }

    public string Text { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }
}
