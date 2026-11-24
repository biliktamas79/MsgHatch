using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Design;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using MsgHatch.Data.EFCore.PostgreSql;
using MsgHatch.Data.EFCore.SqlServer;

namespace MsgHatch.Data.EFCore.Tests.Integration.TestTypes;

/// <summary>
/// The C# generator of the migrations of a provider package (D320). The EF Core tools write the schema and the collation
/// of the design time into the code. This generator writes <c>Schema</c> and <c>TextCollation</c> of
/// <see cref="EntityFrameworkStoreMigration"/> and <see cref="EntityFrameworkStoreModelSnapshot"/> in their place, so
/// each migration takes the schema and the collation of the options at run time. The classes are internal, so they are
/// not a part of the public API of the package.
/// </summary>
public sealed class StoreMigrationsCodeGenerator(
    MigrationsCodeGeneratorDependencies dependencies, CSharpMigrationsGeneratorDependencies csharpDependencies)
    : CSharpMigrationsGenerator(dependencies, csharpDependencies)
{
    private static readonly string[] _dialectCollations =
        [SqlServerStoreDialect.Instance.TextCollation!, PostgreSqlStoreDialect.Instance.TextCollation!];

    public override string GenerateMigration(
        string? migrationNamespace, string migrationName, IReadOnlyList<MigrationOperation> upOperations,
        IReadOnlyList<MigrationOperation> downOperations) =>
        WithStoreOptions(base.GenerateMigration(migrationNamespace, migrationName, upOperations, downOperations))
            .Replace(" : Migration", " : EntityFrameworkStoreMigration", StringComparison.Ordinal);

    public override string GenerateMetadata(
        string? migrationNamespace, Type contextType, string migrationName, string migrationId, IModel targetModel) =>
        WithStoreOptions(base.GenerateMetadata(migrationNamespace, contextType, migrationName, migrationId, targetModel));

    public override string GenerateSnapshot(string? modelSnapshotNamespace, Type contextType, string modelSnapshotName, IModel model) =>
        WithStoreOptions(base.GenerateSnapshot(modelSnapshotNamespace, contextType, modelSnapshotName, model))
            .Replace(" : ModelSnapshot", " : EntityFrameworkStoreModelSnapshot", StringComparison.Ordinal);

    private const string _baseNamespace = "using MsgHatch.Data.EFCore;";

    private static string WithStoreOptions(string code) =>
        WithBaseNamespace(_dialectCollations.Aggregate(
            code.Replace($"\"{EntityFrameworkStoreOptions.DefaultSchema}\"", "Schema", StringComparison.Ordinal)
                .Replace("public partial class", "internal partial class", StringComparison.Ordinal),
            static (text, collation) => text.Replace($"\"{collation}\"", "TextCollation", StringComparison.Ordinal)));

    private static string WithBaseNamespace(string code) =>
        code.Contains(_baseNamespace, StringComparison.Ordinal)
            ? code
            : code.Replace("#nullable disable", $"{_baseNamespace}\n\n#nullable disable", StringComparison.Ordinal);
}
