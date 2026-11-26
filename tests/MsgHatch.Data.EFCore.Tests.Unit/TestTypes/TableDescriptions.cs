using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace MsgHatch.Data.EFCore.Tests.Unit.TestTypes;

/// <summary>A table of the model of a context as text, with no connection, so that a test compares two tables whole.</summary>
public static class TableDescriptions
{
    /// <summary>
    /// The table whose name ends with <paramref name="name"/>: its schema and name, each column with its store type, its
    /// null rule, its collation, its value converter, and its annotations, the primary key, each index, and the annotations
    /// of the table.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <param name="name">The end of the table name, for example <c>Inbox</c> for <c>Inbox</c> and <c>MsgHatchInbox</c>.</param>
    /// <param name="skippedAnnotations">The start of the names of the annotations to leave out, or null for none.</param>
    public static string[] Of(DbContext context, string name, string? skippedAnnotations = null)
    {
        var table = TableOf(context, name);
        return
        [
            $"table {table.Schema}.{table.Name}",
            .. table.Columns.Select(column =>
                $"column {column.Name} {column.StoreType} {(column.IsNullable ? "null" : "not null")} {column.Collation} {ConverterOf(column)} {AnnotationsOf(column, skippedAnnotations)}"),
            $"key {string.Join(",", table.PrimaryKey!.Columns.Select(static column => column.Name))}",
            .. table.Indexes.Select(static index =>
                $"index {index.Name} {(index.IsUnique ? "unique" : "")} {string.Join(",", index.Columns.Select(static column => column.Name))}")
                .Order(StringComparer.Ordinal),
            $"annotations {AnnotationsOf(table, skippedAnnotations)}",
        ];
    }

    /// <summary>The table of the model whose name ends with <paramref name="name"/>.</summary>
    public static ITable TableOf(DbContext context, string name) =>
        context.GetService<IDesignTimeModel>().Model.GetRelationalModel().Tables.Single(table => table.Name.EndsWith(name, StringComparison.Ordinal));

    private static string? ConverterOf(IColumn column) =>
        column.PropertyMappings.Single().Property.GetTypeMapping().Converter?.GetType().Name;

    private static string AnnotationsOf(IAnnotatable item, string? skipped) =>
        string.Join(";", item.GetAnnotations()
            .Where(annotation => skipped is null || !annotation.Name.StartsWith(skipped, StringComparison.Ordinal))
            .Select(static annotation => $"{annotation.Name}={annotation.Value}")
            .Order(StringComparer.Ordinal));
}
