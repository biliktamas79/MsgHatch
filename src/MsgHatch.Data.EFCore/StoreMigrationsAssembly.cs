using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace MsgHatch.Data.EFCore;

/// <summary>
/// The migrations of a MsgHatch context (D320): the migrations and the model snapshot of the context in the migrations
/// assembly of its options, or else in the assembly of the context. A migration or a snapshot of a provider package gets
/// the schema and the collation of the options of the context. A migration that a host generated runs unchanged.
/// </summary>
internal sealed class StoreMigrationsAssembly : IMigrationsAssembly
{
    private readonly Type _contextType;
    private readonly IMigrationsIdGenerator _idGenerator;
    private readonly StoreOptionsExtension _options;
    private IReadOnlyDictionary<string, TypeInfo>? _migrations;
    private Lazy<ModelSnapshot?>? _snapshot;

    public StoreMigrationsAssembly(ICurrentDbContext currentContext, IDbContextOptions options, IMigrationsIdGenerator idGenerator)
    {
        _contextType = currentContext.Context.GetType();
        var relational = RelationalOptionsExtension.Extract(options);
        Assembly = relational.MigrationsAssemblyObject
            ?? (relational.MigrationsAssembly is { } name ? Assembly.Load(new AssemblyName(name)) : _contextType.Assembly);
        _idGenerator = idGenerator;
        _options = StoreOptionsExtension.Of(options);
    }

    public Assembly Assembly { get; }

    public IReadOnlyDictionary<string, TypeInfo> Migrations => _migrations ??= FindMigrations();

    public ModelSnapshot? ModelSnapshot => (_snapshot ??= new Lazy<ModelSnapshot?>(CreateSnapshot)).Value;

    public string? FindMigrationId(string nameOrId) =>
        Migrations.Keys.FirstOrDefault(_idGenerator.IsValidId(nameOrId)
            ? id => string.Equals(id, nameOrId, StringComparison.OrdinalIgnoreCase)
            : id => string.Equals(_idGenerator.GetName(id), nameOrId, StringComparison.OrdinalIgnoreCase));

    public Migration CreateMigration(TypeInfo migrationClass, string activeProvider)
    {
        ArgumentNullException.ThrowIfNull(migrationClass);
        var migration = (Migration)Activator.CreateInstance(migrationClass.AsType())!;
        migration.ActiveProvider = activeProvider;
        (migration as EntityFrameworkStoreMigration)?.UseOptions(_options);
        return migration;
    }

    private SortedList<string, TypeInfo> FindMigrations()
    {
        var migrations = new SortedList<string, TypeInfo>(StringComparer.Ordinal);
        foreach (var type in TypesOfTheContext(typeof(Migration)))
        {
            if (type.GetCustomAttribute<MigrationAttribute>()?.Id is { } id)
            {
                migrations.Add(id, type);
            }
        }

        return migrations;
    }

    private ModelSnapshot? CreateSnapshot()
    {
        var snapshot = TypesOfTheContext(typeof(ModelSnapshot)).Select(static type => (ModelSnapshot)Activator.CreateInstance(type.AsType())!).FirstOrDefault();
        (snapshot as EntityFrameworkStoreModelSnapshot)?.UseOptions(_options);
        return snapshot;
    }

    private IEnumerable<TypeInfo> TypesOfTheContext(Type baseType) =>
        Assembly.DefinedTypes.Where(type => type is { IsAbstract: false, IsGenericTypeDefinition: false } && type.IsSubclassOf(baseType)
            && type.GetCustomAttribute<DbContextAttribute>()?.ContextType == _contextType);
}
