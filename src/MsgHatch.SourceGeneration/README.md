# MsgHatch.SourceGeneration

The source generators of [MsgHatch](https://github.com/biliktamas79/MsgHatch). This project is not a NuGet package. The package `MsgHatch.Templates` carries its assembly in `analyzers/dotnet/cs`, so each project that references `MsgHatch.Templates` runs the generators.

The assembly has two generators:
- The placeholder payload generator. It adds `IPlaceholderPayload<TSelf>` and a placeholder writer to each `partial` type with the attribute `[PlaceholderPayload]` of `MsgHatch.Templates`, so the placeholder engine reads the properties with no boxing and no reflection. A type that is not `partial` gives the error `MSGHATCH001`.
- The HTTP API client generator. It writes the HTTP calls of an internal interface with the attribute `[HttpApi]`. The client packages `MsgHatch.Api.Client`, `MsgHatch.Subscriptions.Api.Client`, and `MsgHatch.Templates.Api.Client` use it at their own build, so their packages hold the generated code and carry no generator. The rules `MSGHATCH002` to `MSGHATCH010` report an interface that the generator cannot write.

The HTTP API client generator adds its internal attributes of the namespace `MsgHatch.Http` to each compilation that runs it. A consumer of `MsgHatch.Templates` does not use them, and an internal type does not conflict with another assembly.

`AnalyzerReleases.Shipped.md` lists the rules of each release.

## Version history

### 0.1.0 (2026-11-22)

- The first version: the placeholder payload generator with the rule `MSGHATCH001`, and the HTTP API client generator with the rules `MSGHATCH002` to `MSGHATCH010`.
