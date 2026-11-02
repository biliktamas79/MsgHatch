using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MsgHatch.SourceGeneration;

/// <summary>
/// Adds a placeholder writer to each type with <c>[PlaceholderPayload]</c> (D18, section 4.10). The writer writes each
/// property with no boxing and no reflection. A type that is not <c>partial</c> gets the diagnostic <c>MSGHATCH001</c>.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class PlaceholderPayloadGenerator : IIncrementalGenerator
{
    private const string _attributeName = "MsgHatch.Email.Placeholders.PlaceholderPayloadAttribute";

    private static readonly DiagnosticDescriptor _notPartial = new(
        "MSGHATCH001",
        "A placeholder payload type is not partial",
        "The placeholder payload type '{0}' is not partial, so the generator cannot add its placeholder writer",
        "MsgHatch",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <inheritdoc/>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var payloads = context.SyntaxProvider.ForAttributeWithMetadataName(
            _attributeName,
            static (node, _) => node is TypeDeclarationSyntax,
            static (attributeContext, _) => OutputOf(attributeContext));
        context.RegisterSourceOutput(payloads, static (sourceContext, output) => output.AddTo(sourceContext));
    }

    private static PayloadOutput OutputOf(GeneratorAttributeSyntaxContext context)
    {
        var type = (INamedTypeSymbol)context.TargetSymbol;
        var declaration = (TypeDeclarationSyntax)context.TargetNode;
        return declaration.Modifiers.Any(SyntaxKind.PartialKeyword)
            ? new PayloadOutput(HintNameOf(type), WriterSource.For(type, context.SemanticModel.Compilation), null, type.Name)
            : new PayloadOutput(HintNameOf(type), null, declaration.Identifier.GetLocation(), type.Name);
    }

    private static string HintNameOf(INamedTypeSymbol type) =>
        new string(type.ToDisplayString().Select(static c => char.IsLetterOrDigit(c) || c == '.' ? c : '_').ToArray()) + ".PlaceholderWriter.g.cs";

    private sealed record PayloadOutput(string HintName, string? Source, Location? NotPartialLocation, string TypeName)
    {
        public void AddTo(SourceProductionContext context)
        {
            if (Source is null)
            {
                context.ReportDiagnostic(Diagnostic.Create(_notPartial, NotPartialLocation, TypeName));
            }
            else
            {
                context.AddSource(HintName, Source);
            }
        }
    }
}
