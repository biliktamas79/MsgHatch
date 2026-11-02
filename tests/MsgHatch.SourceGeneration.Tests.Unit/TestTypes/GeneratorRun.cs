using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using MsgHatch.Email.Placeholders;

namespace MsgHatch.SourceGeneration.Tests.Unit.TestTypes;

/// <summary>Runs the generator with <see cref="CSharpGeneratorDriver"/> on source text, and compiles the result.</summary>
public sealed record GeneratorRun(IReadOnlyList<Diagnostic> GeneratorDiagnostics, IReadOnlyList<Diagnostic> CompileErrors, IReadOnlyList<string> HintNames)
{
    private static readonly MetadataReference[] _references =
    [
        .. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator).Select(static path => MetadataReference.CreateFromFile(path)),
        MetadataReference.CreateFromFile(typeof(PlaceholderPayloadAttribute).Assembly.Location),
    ];

    public static GeneratorRun Of(string source, CancellationToken ct)
    {
        var compilation = CSharpCompilation.Create(
            "GeneratorInput",
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest), cancellationToken: ct)],
            _references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        var driver = CSharpGeneratorDriver.Create(new PlaceholderPayloadGenerator())
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics, ct);
        return new GeneratorRun(
            diagnostics,
            [.. output.GetDiagnostics(ct).Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)],
            [.. driver.GetRunResult().GeneratedTrees.Select(static tree => Path.GetFileName(tree.FilePath))]);
    }
}
