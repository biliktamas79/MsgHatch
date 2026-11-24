# MsgHatch.Templates.Liquid

The Liquid template engine of [MsgHatch](https://github.com/biliktamas79/MsgHatch), on Fluid. The package implements `ITemplateEngine<TPayload>` of `MsgHatch.Templates` with loops, conditions, filters, and nested properties. The engine reads the templates through the template selector of `MsgHatch.Templates`, so the stores and the fallback are the same as for the placeholder engine.

## Install

```shell
dotnet add package MsgHatch.Templates.Liquid
```

## Setup

The package has no setup method. A registration selects the engine with `LiquidTemplates.Engine`, for example on the email channel of `MsgHatch.Email`:

[//]: # (snippet: email)
```csharp
.WithPayload<OrderEmail, OrderEmailLoader>(p => p.AsEmail("OrderEmail", LiquidTemplates.Engine))
```

The payload type of a Liquid template needs no attribute. A template reads its properties by name, for example `{{ OrderNumber }}`, `{% for line in Lines %}`, and `{% if Total > 100 %}`. An html part encodes each value. The engine has no partials: `render` and `include` are not available.

The package has no options.

## Version history

### 0.2.1 (2026-11-24)

- The package depends on the latest stable version of `Fluid.Core` within the major 2 at the time of the pack, not on 2.41.1. The API does not change.

### 0.1.0 (2026-11-22)

- The first version: the Liquid template engine on Fluid with `LiquidTemplates.Engine`, with the template check and the render of a draft for the preview.
