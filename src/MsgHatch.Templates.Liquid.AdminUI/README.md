# MsgHatch.Templates.Liquid.AdminUI

The Liquid template editor of the template admin pages of [MsgHatch](https://github.com/biliktamas79/MsgHatch). The editor highlights the Liquid syntax, and shows the problems of the template check of the API host at their line and column.

## Install

```shell
dotnet add package MsgHatch.Templates.Liquid.AdminUI
```

## Setup

[//]: # (snippet: setup)
```csharp
services.AddMsgHatchTemplatesAdminUI()
    .AddLiquidTemplateEditor();
```

`AddLiquidTemplateEditor` registers the editor for the engine `liquid` of `MsgHatch.Templates.Liquid`, and `TimeProvider.System` when the host has no time provider. The host loads the bundle of the scoped styles `{assembly}.styles.css`. The package has no options.

## Version history

### 0.1.0 (2026-11-22)

- The first version: the Liquid template editor with `AddLiquidTemplateEditor`.
