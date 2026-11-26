using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace MsgHatch.Data.EFCore.Model;

/// <summary>
/// The column rules of the EF Core stores (section 4.5, D178, D318, D319), which the subscription store shares: each
/// time is a UTC <see cref="DateTime"/> in the time type of the provider, each text column has the collation of the
/// options, or else of the dialect, or else of the database, an id is <c>nvarchar(256)</c>, a name is
/// <c>varchar(256)</c>, a <see cref="Guid"/> is <c>char(36)</c> in the format <c>D</c>, an enum value is
/// <c>varchar(32)</c>, and a JSON column has the JSON type of the options or the dialect, or else the text type of the
/// provider (D323). The model has no provider name.
/// </summary>
internal static class StoreModel
{
    private const int _maxLength = 256;

    private const string _tablePrefix = "MsgHatch";

    /// <summary>
    /// Puts each table of a model into the schema of the options (D321). With the dialect of a database with no schemas,
    /// each table gets the prefix <c>MsgHatch</c> and no schema, for example <c>MsgHatchOutbox</c> (D326). The names of the
    /// indexes do not change, because MySQL keeps the index names for each table.
    /// </summary>
    /// <param name="builder">The model of a context, after each table has its name.</param>
    /// <param name="options">The MsgHatch options of the context.</param>
    public static void PlaceTables(ModelBuilder builder, StoreOptionsExtension options)
    {
        if (options.HasSchemas)
        {
            builder.HasDefaultSchema(options.Schema);
            return;
        }

        foreach (var entity in builder.Model.GetEntityTypes())
        {
            entity.SetTableName(TableNameOf(entity.GetTableName()!, options));
        }
    }

    /// <summary>A table name with no prefix in a schema, or with the prefix <c>MsgHatch</c> on a database with no schemas (D326).</summary>
    public static string TableNameOf(string name, StoreOptionsExtension options) => options.HasSchemas ? name : _tablePrefix + name;

    /// <param name="builder">The conventions of a context.</param>
    /// <param name="textCollation">The collation of each text column, or null for the collation of the database.</param>
    public static void ConfigureConventions(ModelConfigurationBuilder builder, string? textCollation)
    {
        builder.Properties<DateTime>().HaveConversion<UtcTimeConverter>();
        if (textCollation is not null)
        {
            builder.Properties<string>().UseCollation(textCollation);
        }
    }

    /// <summary>
    /// The rules of <see cref="ConfigureConventions"/> on one entity type only, for a table in a context of the host
    /// (D331): each time gets the kind UTC, and each text column gets the collation. So the other tables and the
    /// conventions of the host do not change. Call it before the mapping of the columns, so that a JSON column ends with
    /// no collation.
    /// </summary>
    /// <param name="entity">The entity type, with its properties.</param>
    /// <param name="textCollation">The collation of each text column, or null for the collation of the database.</param>
    public static void UseColumnRules(IMutableEntityType entity, string? textCollation)
    {
        foreach (var property in entity.GetProperties())
        {
            if ((Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType) == typeof(DateTime))
            {
                property.SetValueConverter(typeof(UtcTimeConverter));
            }
            else if (property.ClrType == typeof(string) && textCollation is not null)
            {
                property.SetCollation(textCollation);
            }
        }
    }

    public static PropertyBuilder<string> IsId(this PropertyBuilder<string> property) => property.HasMaxLength(_maxLength);

    public static PropertyBuilder<TText> IsName<TText>(this PropertyBuilder<TText> property) =>
        property.HasMaxLength(_maxLength).IsUnicode(false);

    public static PropertyBuilder<TText> IsGuid<TText>(this PropertyBuilder<TText> property) =>
        property.HasMaxLength(36).IsUnicode(false).IsFixedLength();

    public static PropertyBuilder<string> IsEnum(this PropertyBuilder<string> property) => property.HasMaxLength(32).IsUnicode(false);

    /// <summary>
    /// A JSON column gets no collation, because no query compares or sorts a document, and PostgreSQL refuses a collation
    /// on <c>json</c> with the error 42804.
    /// </summary>
    /// <param name="property">A JSON column.</param>
    /// <param name="jsonColumnType">The JSON type of the options or the dialect, or null for the text type of the provider.</param>
    public static PropertyBuilder<TText> IsJson<TText>(this PropertyBuilder<TText> property, string? jsonColumnType) =>
        (jsonColumnType is null ? property : property.HasColumnType(jsonColumnType)).UseCollation(null);

    /// <summary>
    /// A provider can refuse a time with no kind, and a database keeps no kind of a time. So each time gets the kind UTC on
    /// a write and on a read (section 4.14, D319).
    /// </summary>
    private sealed class UtcTimeConverter() : ValueConverter<DateTime, DateTime>(
        static time => DateTime.SpecifyKind(time, DateTimeKind.Utc), static time => DateTime.SpecifyKind(time, DateTimeKind.Utc));
}
