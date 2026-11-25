using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Data.EFCore.Providers;

namespace MsgHatch.Data.EFCore.Model;

/// <summary>
/// The MsgHatch options of a <see cref="DbContext"/> of the stores (D318): the schema of
/// <see cref="EntityFrameworkStoreOptions.Schema"/>, the collation of the text columns, the type of the JSON columns, and
/// the dialect of a provider package. EF Core keeps one model cache for each internal service provider, and the schema, the
/// collation, the JSON type, the type of the dialect, and whether the database has schemas are part of the hash of the
/// service provider. So two schemas, two collations, or two JSON types in one process give two models, and a pooled context
/// keeps no state of its own.
/// </summary>
/// <param name="schema">The schema.</param>
/// <param name="textCollation">The collation of each text column, or null for the collation of the database.</param>
/// <param name="jsonColumnType">The type of each JSON column, or null for the text type of the provider.</param>
/// <param name="dialect">The dialect of a provider package, or null.</param>
internal sealed class StoreOptionsExtension(string schema, string? textCollation, string? jsonColumnType, IEntityFrameworkStoreDialect? dialect)
    : IDbContextOptionsExtension
{
    private static readonly StoreOptionsExtension _default =
        new(EntityFrameworkStoreOptions.DefaultSchema, textCollation: null, jsonColumnType: null, dialect: null);

    private DbContextOptionsExtensionInfo? _info;

    public string Schema { get; } = schema;

    public string? TextCollation { get; } = textCollation;

    public string? JsonColumnType { get; } = jsonColumnType;

    public IEntityFrameworkStoreDialect? Dialect { get; } = dialect;

    /// <summary>Whether the database has schemas: false only with the dialect of a database with no schemas (D326).</summary>
    public bool HasSchemas => Dialect?.HasSchemas ?? true;

    public DbContextOptionsExtensionInfo Info => _info ??= new ExtensionInfo(this);

    /// <summary>The MsgHatch options of a context, or the default schema with no collation and no dialect.</summary>
    public static StoreOptionsExtension Of(IDbContextOptions options) => options.FindExtension<StoreOptionsExtension>() ?? _default;

    public static StoreOptionsExtension Of(DbContext context) => Of(context.GetService<IDbContextOptions>());

    /// <summary>
    /// Puts a dialect into the options of a context. With no collation or JSON type yet, the context gets the collation and
    /// the JSON type of the dialect.
    /// </summary>
    public static void UseDialect(DbContextOptionsBuilder builder, IEntityFrameworkStoreDialect dialect)
    {
        var current = Of(builder.Options);
        Add(builder, new StoreOptionsExtension(
            current.Schema, current.TextCollation ?? dialect.TextCollation, current.JsonColumnType ?? dialect.JsonColumnType, dialect));
    }

    /// <summary>
    /// Puts the schema, the collation, and the JSON type of the options of the stores into the options of a context, and
    /// keeps its dialect. With no collation or JSON type in the options, the context gets the one of its dialect.
    /// </summary>
    public static void UseOptions(DbContextOptionsBuilder builder, EntityFrameworkStoreOptions options)
    {
        var dialect = Of(builder.Options).Dialect;
        Add(builder, new StoreOptionsExtension(
            options.Schema, options.TextCollation ?? dialect?.TextCollation, options.JsonColumnType ?? dialect?.JsonColumnType, dialect));
    }

    public void ApplyServices(IServiceCollection services)
    {
    }

    public void Validate(IDbContextOptions options)
    {
    }

    private static void Add(DbContextOptionsBuilder builder, StoreOptionsExtension extension) =>
        ((IDbContextOptionsBuilderInfrastructure)builder).AddOrUpdateExtension(extension);

    private sealed class ExtensionInfo(StoreOptionsExtension extension) : DbContextOptionsExtensionInfo(extension)
    {
        public override bool IsDatabaseProvider => false;

        public override string LogFragment =>
            $"MsgHatchSchema={extension.Schema} MsgHatchTextCollation={extension.TextCollation} MsgHatchJsonColumnType={extension.JsonColumnType} ";

        private string Schema => extension.Schema;

        private string? TextCollation => extension.TextCollation;

        private string? JsonColumnType => extension.JsonColumnType;

        private Type? DialectType => extension.Dialect?.GetType();

        private bool HasSchemas => extension.HasSchemas;

        public override int GetServiceProviderHashCode() =>
            HashCode.Combine(StringComparer.Ordinal.GetHashCode(Schema), TextCollation, JsonColumnType, DialectType, HasSchemas);

        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other) =>
            other is ExtensionInfo info
            && string.Equals(info.Schema, Schema, StringComparison.Ordinal)
            && string.Equals(info.TextCollation, TextCollation, StringComparison.Ordinal)
            && string.Equals(info.JsonColumnType, JsonColumnType, StringComparison.Ordinal)
            && info.DialectType == DialectType
            && info.HasSchemas == HasSchemas;

        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo)
        {
            debugInfo["MsgHatch:Schema"] = extension.Schema;
            debugInfo["MsgHatch:TextCollation"] = extension.TextCollation ?? string.Empty;
            debugInfo["MsgHatch:JsonColumnType"] = extension.JsonColumnType ?? string.Empty;
        }
    }
}
