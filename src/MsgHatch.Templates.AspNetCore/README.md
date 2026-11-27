# MsgHatch.Templates.AspNetCore

The ASP.NET Core HTTP endpoints of the template management API of [MsgHatch](https://github.com/biliktamas79/MsgHatch). The endpoints call `ITemplateQueries` and `ITemplateCommands` of `MsgHatch.Templates`, as minimal API endpoints with a source-generated request delegate: the list, the read, the write, the delete, and the preview of a template.

A write changes the template store of the host. Protect the endpoints with an authorization policy.

## Install

```shell
dotnet add package MsgHatch.Templates.AspNetCore
```

## Setup

The host selects a template store of `MsgHatch.Templates` that can write, for example `UseFileTemplates`, and then maps the endpoints:

[//]: # (snippet: setup)
```csharp
app.MapMsgHatchTemplates()
    .RequireAuthorization("TemplateAdmin");
```

The default prefix is `/templates`, and `MapMsgHatchTemplates("/admin/templates")` sets another one. The HTTP client of `MsgHatch.Templates.Api.Client` calls the same routes. The package has no options.

## Version history

### 0.9.0 (2026-11-26)

- The package compiles only the half of the internal error mapping that gives the HTTP status of an error type, so it has no code that it does not call. The API and the behavior do not change.

### 0.7.1 (2026-11-26)

- The package has the MsgHatch icon. The code and the API do not change.

### 0.1.0 (2026-11-22)

- The first version: the HTTP endpoints of the template management API with the preview of a template, with `MapMsgHatchTemplates`.
