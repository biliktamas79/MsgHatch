# MsgHatch.Api.Contracts

The HTTP contracts of the registry API and of the delivery API of [MsgHatch](https://github.com/biliktamas79/MsgHatch): the request and response bodies, their mappings to the types of `MsgHatch.Domain`, and their source-generated JSON contexts. It also has the error metadata of all MsgHatch HTTP APIs: `ErrorTypeBody` and its JSON context `ApiErrorJson`.

The endpoints of `MsgHatch.AspNetCore` and the client of `MsgHatch.Api.Client` share these bodies, so a host with no MsgHatch package can also call the API with them. The package has no HTTP concern, no setup method, and no options.

## Install

```shell
dotnet add package MsgHatch.Api.Contracts
```

## Version history

### 0.1.0 (2026-11-22)

- The first version: the bodies of the registry API and of the delivery API with their JSON contexts, and the error metadata of the HTTP APIs.
