using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace MsgHatch.Data.EFCore;

/// <summary>
/// The column rules of the EF Core stores (section 4.5, D178), which the subscription store shares: each time is UTC,
/// each text compares with a binary collation, so an id or a name is case-sensitive and sorts in ordinal order, an id is
/// <c>nvarchar(256)</c>, a name is <c>varchar(256)</c>, a <see cref="Guid"/> is <c>char(36)</c> in the format <c>D</c>,
/// and an enum value is <c>varchar(32)</c>. Only a provider with the SQL statements of the stores gets the collation.
/// </summary>
internal static class StoreModel
{
    public const string SqlServerProvider = "Microsoft.EntityFrameworkCore.SqlServer";

    private const string _binaryCollation = "Latin1_General_100_BIN2";

    private const int _maxLength = 256;

    /// <summary>Whether the stores have their SQL statements for the provider (D120, D175).</summary>
    public static bool HasStatements(string? providerName) => providerName == SqlServerProvider;

    public static void ConfigureConventions(ModelConfigurationBuilder builder, string? providerName)
    {
        builder.Properties<DateTime>().HaveConversion<UtcTimeConverter>();
        if (HasStatements(providerName))
        {
            builder.Properties<string>().UseCollation(_binaryCollation);
        }
    }

    public static PropertyBuilder<string> IsId(this PropertyBuilder<string> property) => property.HasMaxLength(_maxLength);

    public static PropertyBuilder<TText> IsName<TText>(this PropertyBuilder<TText> property) =>
        property.HasMaxLength(_maxLength).IsUnicode(false);

    public static PropertyBuilder<string> IsGuid(this PropertyBuilder<string> property) =>
        property.HasMaxLength(36).IsUnicode(false).IsFixedLength();

    public static PropertyBuilder<string> IsEnum(this PropertyBuilder<string> property) => property.HasMaxLength(32).IsUnicode(false);

    /// <summary>The database keeps no kind of a time, so a time that the store reads gets the kind UTC (section 4.14).</summary>
    private sealed class UtcTimeConverter() : ValueConverter<DateTime, DateTime>(
        static time => time, static time => DateTime.SpecifyKind(time, DateTimeKind.Utc));
}
