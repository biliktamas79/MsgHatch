using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace MsgHatch.Data.EFCore.MySql;

/// <summary>
/// The model of a MsgHatch context on MySQL: the model of the base package, with two changes.
/// <list type="bullet">
/// <item>The character set <c>utf8mb4</c> is on each table, and not on the database. A column with no character set of its
/// own takes the one of its table. The provider puts its default character set on the database too, so a migration starts
/// with <c>ALTER DATABASE CHARACTER SET utf8mb4</c>, which changes the default of each later table of the host in its own
/// database (D326).</item>
/// <item>A text of a fixed length, for example an id in the format of a <see cref="Guid"/>, is <c>varchar</c>, not
/// <c>char</c>. MySqlConnector reads a <c>CHAR(36)</c> column as a <see cref="Guid"/>, and the provider maps the store type
/// <c>char(36)</c> of a migration to a <see cref="Guid"/>, not to a text.</item>
/// </list>
/// </summary>
/// <param name="dependencies">The dependencies of the customizer.</param>
internal sealed class MySqlStoreModelCustomizer(ModelCustomizerDependencies dependencies) : RelationalModelCustomizer(dependencies)
{
    private const string _charSet = "utf8mb4";

    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);
        modelBuilder.HasCharSet(_charSet, DelegationModes.ApplyToTables);
        foreach (var text in FixedLengthTexts(modelBuilder.Model))
        {
            text.SetIsFixedLength(false);
        }
    }

    private static List<IMutableProperty> FixedLengthTexts(IMutableModel model) =>
        [.. model.GetEntityTypes().SelectMany(static entity => entity.GetProperties())
            .Where(static property => property.ClrType == typeof(string) && property.IsFixedLength() == true)];
}
