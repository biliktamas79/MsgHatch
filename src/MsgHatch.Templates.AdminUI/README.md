# MsgHatch.Templates.AdminUI

The Blazor admin pages of the templates of [MsgHatch](https://github.com/biliktamas79/MsgHatch): the list, the view, and the edit page of a template, with the preview of a draft with the sample payload of its registration. The edit page has an extension point for the editor of each engine, and a plain text editor for the others.

The pages call `ITemplateQueries` and `ITemplateCommands` of the host, usually the HTTP client of `MsgHatch.Templates.Api.Client`. The pages use the layout of `MsgHatch.AdminUI`.

## Install

```shell
dotnet add package MsgHatch.Templates.AdminUI
```

## Setup

The host selects the API client, for example `UseTemplateApiClient` of `MsgHatch.Templates.Api.Client` with `MsgHatchRoles.None`, and adds the pages:

[//]: # (snippet: setup)
```csharp
services.AddMsgHatchTemplatesAdminUI();
```

The host maps the pages with the assembly of `TemplateAdminRoutes` as an additional assembly of its router, as `MsgHatch.AdminUI` describes. `AddTemplateEditor<TEditor>(engine)` registers the editor of one engine, for example `AddLiquidTemplateEditor` of `MsgHatch.Templates.Liquid.AdminUI`. An editor derives from `TemplateEditorBase`. The package has no options.

## Version history

### 0.7.1 (2026-11-26)

- The package has the MsgHatch icon. The code and the API do not change.

### 0.1.0 (2026-11-22)

- The first version: the template list, view, and edit pages with the preview, and the editor extension point, with `AddMsgHatchTemplatesAdminUI`.
