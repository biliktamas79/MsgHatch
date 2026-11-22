# MsgHatch.Email.AmazonSES

The Amazon SES client of the [MsgHatch](https://github.com/biliktamas79/MsgHatch) email channel. The package implements `IEmailClient` of `MsgHatch.Email` with the SES v2 API of the AWS SDK.

SES has no idempotency key. If the process stops after SES accepts an email and before the outbox records the try, the email is sent again.

## Install

```shell
dotnet add package MsgHatch.Email.AmazonSES
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

### 0.1.0 (2026-11-22)

- The first version: the Amazon SES email client with `UseAmazonSes`. A throttle error of SES gives a rate limit error with the wait that SES asks for.
