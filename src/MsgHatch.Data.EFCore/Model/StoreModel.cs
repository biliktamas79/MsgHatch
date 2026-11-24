using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace MsgHatch.Data.EFCore.Model;

/// <summary>
/// The column rules of the EF Core stores (section 4.5, D178, D318, D319), which the subscription store shares: each
/// time is a UTC <see cref="DateTime"/> in the time type of the provider, each text column has the collation of the
/// options, or else of the dialect, or else of the database, an id is <c>nvarchar(256)</c>, a name is
/// <c>varchar(256)</c>, a <see cref="Guid"/> is <c>char(36)</c> in the format <c>D</c>, and an enum value is
/// <c>varchar(32)</c>. The model has no provider name.
/// </summary>
internal static class StoreModel
{
    private const int _maxLength = 256;

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

    public static PropertyBuilder<string> IsId(this PropertyBuilder<string> property) => property.HasMaxLength(_maxLength);

    public static PropertyBuilder<TText> IsName<TText>(this PropertyBuilder<TText> property) =>
        property.HasMaxLength(_maxLength).IsUnicode(false);

    public static PropertyBuilder<TText> IsGuid<TText>(this PropertyBuilder<TText> property) =>
        property.HasMaxLength(36).IsUnicode(false).IsFixedLength();

    public static PropertyBuilder<string> IsEnum(this PropertyBuilder<string> property) => property.HasMaxLength(32).IsUnicode(false);

    /// <summary>
    /// A provider can refuse a time with no kind, and a database keeps no kind of a time. So each time gets the kind UTC on
    /// a write and on a read (section 4.14, D319).
    /// </summary>
    private sealed class UtcTimeConverter() : ValueConverter<DateTime, DateTime>(
        static time => DateTime.SpecifyKind(time, DateTimeKind.Utc), static time => DateTime.SpecifyKind(time, DateTimeKind.Utc));
}
