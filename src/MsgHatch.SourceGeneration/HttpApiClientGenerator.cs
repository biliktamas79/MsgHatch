using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MsgHatch.SourceGeneration;

/// <summary>
/// Writes the HTTP client of each internal interface with <c>[HttpApi]</c> (D236, section 4.23): an internal sealed class
/// with a constructor that takes the <c>HttpClient</c> and the route prefix. Each method sends one
/// request, and gives each failure as an <c>Error</c> of <c>ErrorProblems</c> (D233), so no method throws for an error
/// status, a network error, or a timeout. A member that the generator cannot write gives an error diagnostic.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class HttpApiClientGenerator : IIncrementalGenerator
{
    /// <inheritdoc/>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterPostInitializationOutput(static output =>
        {
            output.AddEmbeddedAttributeDefinition();
            output.AddSource("MsgHatch.Http.Attributes.g.cs", HttpApiSource.Attributes);
        });
        var apis = context.SyntaxProvider.ForAttributeWithMetadataName(
            HttpApiSource.ApiAttribute,
            static (node, _) => node is InterfaceDeclarationSyntax,
            static (attributeContext, _) => HttpApiModel.OutputOf(attributeContext));
        context.RegisterSourceOutput(apis, static (sourceContext, output) => output.AddTo(sourceContext));
    }
}

/// <summary>The source of one HTTP API, or the diagnostics that stop it.</summary>
internal sealed record HttpApiOutput(string HintName, string? Source, ImmutableArray<Diagnostic> Diagnostics)
{
    public void AddTo(SourceProductionContext context)
    {
        foreach (var diagnostic in Diagnostics)
        {
            context.ReportDiagnostic(diagnostic);
        }

        if (Source is not null)
        {
            context.AddSource(HintName, Source);
        }
    }
}
