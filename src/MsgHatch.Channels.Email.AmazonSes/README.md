# MsgHatch.Channels.Email.AmazonSes

The Amazon SES client of the [MsgHatch](https://github.com/biliktamas79/MsgHatch) email channel. The package implements `IEmailClient` of `MsgHatch.Channels.Email` with the SES v2 API of the AWS SDK.

SES has no idempotency key. If the process stops after SES accepts an email and before the outbox records the try, the email is sent again.

## Install

```shell
dotnet add package MsgHatch.Channels.Email.AmazonSes
```

## Setup

[//]: # (snippet: setup)
```csharp
services.AddMsgHatch()
    .UseAmazonSes();
```

`UseAmazonSes` selects the SES client and the email transport. It registers an `IAmazonSimpleEmailServiceV2` client for the region of the options when the host has not registered one, so the AWS SDK gives the credentials. It binds `AmazonSesOptions` from the section `MsgHatch:AmazonSes`, and validates them at the start of the host. A value from code wins over the configuration:

[//]: # (snippet: setup-code)
```csharp
services.AddMsgHatch()
    .UseAmazonSes("noreply@example.com");
```

## Options

[//]: # (options: AmazonSesOptions)
| Option | Type | Default | Rule |
|---|---|---|---|
| `SenderEmail` | `string` | none | Required. The sender address of each email with no address of its own. Only 7-bit ASCII, so a domain with other letters must be in Punycode. |
| `SenderName` | `string?` | none | At most 100 characters. |
| `Region` | `string?` | the region of the AWS SDK configuration | An AWS region, for example `eu-west-1`. |

[//]: # (snippet: options)
```json
{
  "MsgHatch": {
    "AmazonSes": {
      "SenderEmail": "noreply@example.com",
      "SenderName": "Example Shop",
      "Region": "eu-west-1"
    }
  }
}
```

## Version history

### 0.7.1 (2026-11-26)

- The package has the MsgHatch icon. The code and the API do not change.

### 0.6.0 (2026-11-25)

- Breaking: the package id is `MsgHatch.Channels.Email.AmazonSes`, not `MsgHatch.Email.AmazonSES`. Each namespace starts with `MsgHatch.Channels.Email.AmazonSes`, not `MsgHatch.Email.AmazonSES`. The class names and the setup methods do not change, so a host changes only its package reference and its `using` directives.

### 0.2.1 (2026-11-24)

- The package depends on the latest stable version of `AWSSDK.SimpleEmailV2` within the major 4 at the time of the pack, not on 4.0.106.7. The API does not change.

### 0.1.0 (2026-11-22)

- The first version: the Amazon SES email client with `UseAmazonSes`. A throttle error of SES gives a rate limit error with the wait that SES asks for.
