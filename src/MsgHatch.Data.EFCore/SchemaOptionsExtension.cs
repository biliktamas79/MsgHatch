using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace MsgHatch.Data.EFCore;

/// <summary>
/// The schema of the tables of a MsgHatch <see cref="DbContext"/>, from <see cref="EntityFrameworkStoreOptions.Schema"/>.
/// EF Core keeps one model cache for each internal service provider, and the schema is part of the hash of the
/// service provider. So two schemas in one process give two models, and a pooled context keeps no state of its own.
/// </summary>
/// <param name="schema">The schema.</param>
internal sealed class SchemaOptionsExtension(string schema) : IDbContextOptionsExtension
{
    private DbContextOptionsExtensionInfo? _info;

    public string Schema { get; } = schema;

    public DbContextOptionsExtensionInfo Info => _info ??= new ExtensionInfo(this);

    /// <summary>The schema of a context, or <see cref="EntityFrameworkStoreOptions.DefaultSchema"/> when the options have no schema.</summary>
    public static string SchemaOf(DbContextOptions options) =>
        options.FindExtension<SchemaOptionsExtension>()?.Schema ?? EntityFrameworkStoreOptions.DefaultSchema;

    /// <summary>Puts the schema into the options of a context.</summary>
    public static void Use(DbContextOptionsBuilder builder, string schema) =>
        ((IDbContextOptionsBuilderInfrastructure)builder).AddOrUpdateExtension(new SchemaOptionsExtension(schema));

    public void ApplyServices(IServiceCollection services)
    {
    }

    public void Validate(IDbContextOptions options)
    {
    }

    private sealed class ExtensionInfo(SchemaOptionsExtension extension) : DbContextOptionsExtensionInfo(extension)
    {
        public override bool IsDatabaseProvider => false;

        public override string LogFragment => $"MsgHatchSchema={extension.Schema} ";

        public override int GetServiceProviderHashCode() => StringComparer.Ordinal.GetHashCode(extension.Schema);

        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other) =>
            other is ExtensionInfo info && string.Equals(info.Schema, extension.Schema, StringComparison.Ordinal);

        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo) => debugInfo["MsgHatch:Schema"] = extension.Schema;

        private string Schema => extension.Schema;
    }
}
