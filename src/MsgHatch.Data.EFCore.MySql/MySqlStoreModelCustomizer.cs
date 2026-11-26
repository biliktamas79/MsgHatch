using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace MsgHatch.Data.EFCore.MySql;

/// <summary>
/// The model of a MsgHatch context on MySQL: the model of the base package, with two changes.
/// <list type="bullet">
/// <item>The character set <c>utf8mb4</c> is on each table, and not on the database. A column with no character set of its
/// own takes the one of its table. The provider puts its default character set on the database too, so a migration starts
/// with <c>ALTER DATABASE CHARACTER SET utf8mb4</c>, which changes the default of each later table of the host in its own
/// database (D326).</item>
/// <item>Each table gets the change of <see cref="MySqlStoreDialect.CustomizeEntity"/>: a text of a fixed length is
/// <c>varchar</c>, not <c>char</c>.</item>
/// </list>
/// </summary>
/// <param name="dependencies">The dependencies of the customizer.</param>
internal sealed class MySqlStoreModelCustomizer(ModelCustomizerDependencies dependencies) : RelationalModelCustomizer(dependencies)
{
    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);
        modelBuilder.HasCharSet(MySqlStoreDialect.CharSet, DelegationModes.ApplyToTables);
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            MySqlStoreDialect.Instance.CustomizeEntity(entity);
        }
    }
}
