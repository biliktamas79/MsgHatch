using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using MsgHatch.Templates.Api.Contracts;
using MsgHatch.Templates.Placeholders;

namespace MsgHatch.SourceGeneration.Tests.Unit.TestTypes;

/// <summary>
/// Runs one generator with <see cref="CSharpGeneratorDriver"/> on source text, and compiles the result with the source of
/// <c>ErrorProblems</c> and the contracts packages, which a client package compiles too (D233).
/// </summary>
public sealed record GeneratorRun(
    IReadOnlyList<Diagnostic> GeneratorDiagnostics, IReadOnlyList<Diagnostic> CompileErrors, IReadOnlyList<Diagnostic> CompileWarnings,
    IReadOnlyList<string> HintNames)
{
    private static readonly MetadataReference[] _references =
    [
        .. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Concat([typeof(PlaceholderPayloadAttribute).Assembly.Location, typeof(TemplateEndpointJson).Assembly.Location, typeof(Api.Contracts.ErrorTypeBody).Assembly.Location])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(static path => MetadataReference.CreateFromFile(path)),
    ];

    private static readonly string _errorProblems = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Shared", "ErrorProblems.cs"));

    public static GeneratorRun Of(string source, CancellationToken ct) => Of(source, new PlaceholderPayloadGenerator(), ct);

    public static GeneratorRun Of(string source, IIncrementalGenerator generator, CancellationToken ct)
    {
        var options = new CSharpParseOptions(LanguageVersion.Latest);
        var compilation = CSharpCompilation.Create(
            "GeneratorInput",
            [CSharpSyntaxTree.ParseText(source, options, cancellationToken: ct), CSharpSyntaxTree.ParseText(_errorProblems, options, cancellationToken: ct)],
            _references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        var driver = CSharpGeneratorDriver.Create([generator.AsSourceGenerator()], parseOptions: options)
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics, ct);
        var compiled = output.GetDiagnostics(ct);
        return new GeneratorRun(
            diagnostics,
            [.. compiled.Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)],
            [.. compiled.Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Warning)],
            [.. driver.GetRunResult().GeneratedTrees.Select(static tree => Path.GetFileName(tree.FilePath))]);
    }
}
