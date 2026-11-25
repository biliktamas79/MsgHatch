# MsgHatch.Templates

The template library of [MsgHatch](https://github.com/biliktamas79/MsgHatch), which knows no channel. The package has the template engine contract `ITemplateEngine<TPayload>`, the template stores, the template selector with its fallback, the placeholder engine, and the template management API (`ITemplateQueries` and `ITemplateCommands`). The email channel of `MsgHatch.Channels.Email` renders its parts through it.

The package carries the source generator of MsgHatch. It writes the placeholder writer of each payload type with the attribute `[PlaceholderPayload]`, so the placeholder engine reads the properties with no reflection.

## Install

```shell
dotnet add package MsgHatch.Templates
```

## Setup

A host that renders templates selects one shared template store. The file store reads a folder:

[//]: # (snippet: setup)
```csharp
services.AddMsgHatch()
    .UseFileTemplates();
```

`UseFileTemplates` binds `FileTemplateOptions` from the section `MsgHatch:FileTemplates`, and validates them at the start of the host. A value from code wins over the configuration:

[//]: # (snippet: setup-code)
```csharp
services.AddMsgHatch()
    .UseFileTemplates("Templates");
```

The file of a template is `{MessageType}.{Language}.{Part}.txt` or `.html`, for example `OrderShipped.en.subject.txt` and `OrderShipped.en.body.html`. The template of a scope, for example of one company, is in the folder of the scope: `acme/OrderShipped.en.subject.txt`.

The selector tries the template store of the tenant first, then the shared store, each with the scope and then with no scope, and then the same lookups with the default language. Each part falls back on its own.

Other stores:
- `UseEmbeddedTemplates(assembly, prefix)` reads the embedded resources of an assembly.
- `UseTemplateStore<TStore>()` selects a store of the host.
- `UseTenantTemplateStore<TStore>()` selects a template store for each tenant.

## Options

[//]: # (options: FileTemplateOptions)
| Option | Type | Default | Rule |
|---|---|---|---|
| `RootPath` | `string` | none | Required. The folder of the template files. |

[//]: # (snippet: options)
```json
{
  "MsgHatch": {
    "FileTemplates": {
      "RootPath": "Templates"
    }
  }
}
```

## Placeholder templates

A placeholder is `{{PropertyName}}` of a top-level property of the payload. An html part encodes each value. The payload type has the attribute and is `partial`:

[//]: # (snippet: payload)
```csharp
[PlaceholderPayload]
public sealed partial record OrderShippedEmail(string OrderNumber, decimal Total);
```

A subject template is then, for example, `Your order {{OrderNumber}} is on its way`.

## Version history

### 0.1.0 (2026-11-22)

- The first version: the template engine contract, the file, embedded, and tenant template stores, the template selector, the placeholder engine with its source generator, the template management API, and the sample payloads of the preview.
