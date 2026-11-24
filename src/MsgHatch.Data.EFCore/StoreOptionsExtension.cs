using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace MsgHatch.Data.EFCore;

/// <summary>
/// The MsgHatch options of a <see cref="DbContext"/> of the stores (D318): the schema of
/// <see cref="EntityFrameworkStoreOptions.Schema"/>, the collation of the text columns, and the dialect of a provider
/// package. EF Core keeps one model cache for each internal service provider, and the schema, the collation, and the type
/// of the dialect are part of the hash of the service provider. So two schemas or two collations in one process give two
/// models, and a pooled context keeps no state of its own.
/// </summary>
/// <param name="schema">The schema.</param>
/// <param name="textCollation">The collation of each text column, or null for the collation of the database.</param>
/// <param name="dialect">The dialect of a provider package, or null.</param>
internal sealed class StoreOptionsExtension(string schema, string? textCollation, IEntityFrameworkStoreDialect? dialect) : IDbContextOptionsExtension
{
    private static readonly StoreOptionsExtension _default = new(EntityFrameworkStoreOptions.DefaultSchema, textCollation: null, dialect: null);

    private DbContextOptionsExtensionInfo? _info;

    public string Schema { get; } = schema;

    public string? TextCollation { get; } = textCollation;

    public IEntityFrameworkStoreDialect? Dialect { get; } = dialect;

    public DbContextOptionsExtensionInfo Info => _info ??= new ExtensionInfo(this);

    /// <summary>The MsgHatch options of a context, or the default schema with no collation and no dialect.</summary>
    public static StoreOptionsExtension Of(IDbContextOptions options) => options.FindExtension<StoreOptionsExtension>() ?? _default;

    public static StoreOptionsExtension Of(DbContext context) => Of(context.GetService<IDbContextOptions>());

    /// <summary>Puts a dialect into the options of a context. With no collation yet, the context gets the collation of the dialect.</summary>
    public static void UseDialect(DbContextOptionsBuilder builder, IEntityFrameworkStoreDialect dialect)
    {
        var current = Of(builder.Options);
        Add(builder, new StoreOptionsExtension(current.Schema, current.TextCollation ?? dialect.TextCollation, dialect));
    }

    /// <summary>
    /// Puts the schema and the collation of the options of the stores into the options of a context, and keeps its
    /// dialect. With no collation in the options, the context gets the collation of its dialect.
    /// </summary>
    public static void UseOptions(DbContextOptionsBuilder builder, EntityFrameworkStoreOptions options)
    {
        var dialect = Of(builder.Options).Dialect;
        Add(builder, new StoreOptionsExtension(options.Schema, options.TextCollation ?? dialect?.TextCollation, dialect));
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

        public override string LogFragment => $"MsgHatchSchema={extension.Schema} MsgHatchTextCollation={extension.TextCollation} ";

        private string Schema => extension.Schema;

        private string? TextCollation => extension.TextCollation;

        private Type? DialectType => extension.Dialect?.GetType();

        public override int GetServiceProviderHashCode() =>
            HashCode.Combine(StringComparer.Ordinal.GetHashCode(Schema), TextCollation, DialectType);

        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other) =>
            other is ExtensionInfo info
            && string.Equals(info.Schema, Schema, StringComparison.Ordinal)
            && string.Equals(info.TextCollation, TextCollation, StringComparison.Ordinal)
            && info.DialectType == DialectType;

        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo)
        {
            debugInfo["MsgHatch:Schema"] = extension.Schema;
            debugInfo["MsgHatch:TextCollation"] = extension.TextCollation ?? string.Empty;
        }
    }
}
