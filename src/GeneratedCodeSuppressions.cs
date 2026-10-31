using System.Diagnostics.CodeAnalysis;

[assembly: SuppressMessage(
    "ApiDesign",
    "RS0030",
    Scope = "namespaceanddescendants",
    Target = "~N:Microsoft.Extensions.Configuration.Binder.SourceGeneration",
    Justification = "The configuration binding generator (D65) reads the IConfiguration indexer in its generated code. The ban of D63 is for hand-written code.")]
