using Microsoft.EntityFrameworkCore;
using MsgHatch.Data.EFCore.Messages;
using MsgHatch.Data.EFCore.Model;

namespace MsgHatch.Data.EFCore;

/// <summary>The mapping of the inbox of the EF Core stores in a context of the host (D331).</summary>
public static class MsgHatchModelBuilderExtensions
{
    /// <summary>
    /// Maps the inbox table of the EF Core stores in a context of the host, so that an event goes into the inbox in the
    /// transaction of that context (D331, D332). Call it in <c>OnModelCreating</c> of the context:
    /// <c>modelBuilder.MapMsgHatchInbox(this)</c>. The table has the name, the schema or the prefix, the columns, the key,
    /// and the indexes of the inbox of <see cref="MsgHatchDbContext"/>, with the MsgHatch options of the context, which the
    /// generic setup method of a provider package gives it. The migrations of the host never contain the table, because
    /// the migrations of MsgHatch own it (D320). The schema, the collation, the UTC kind of the times, the JSON type, and
    /// the change of the dialect go on this table only, so the other tables and the conventions of the host do not change.
    /// Each column name is explicit, so a naming convention of the host does not change it.
    /// </summary>
    /// <param name="modelBuilder">The model of the context of the host.</param>
    /// <param name="context">The context of the host, which gives its MsgHatch options.</param>
    /// <returns>The model builder.</returns>
    public static ModelBuilder MapMsgHatchInbox(this ModelBuilder modelBuilder, DbContext context)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        ArgumentNullException.ThrowIfNull(context);
        var options = StoreOptionsExtension.Of(context);
        var inbox = modelBuilder.Entity<InboxRow>();
        StoreModel.UseColumnRules(inbox.Metadata, options.TextCollation);
        MessageModel.Inbox(inbox, options.JsonColumnType);
        inbox.ToTable(
            StoreModel.TableNameOf(MessageModel.InboxTable, options), options.HasSchemas ? options.Schema : null,
            static table => table.ExcludeFromMigrations());
        foreach (var property in inbox.Metadata.GetProperties())
        {
            property.SetColumnName(property.Name);
        }

        options.Dialect?.CustomizeEntity(inbox.Metadata);
        return modelBuilder;
    }
}
