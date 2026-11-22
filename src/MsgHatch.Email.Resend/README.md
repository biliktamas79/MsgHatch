# MsgHatch.Email.Resend

The Resend client of the [MsgHatch](https://github.com/biliktamas79/MsgHatch) email channel. The package implements `IEmailClient` of `MsgHatch.Email` with the HTTP API of Resend. Each send carries an idempotency key, so a send again of the same message within 24 hours gives no second email.

## Install

```shell
dotnet add package MsgHatch.Email.Resend
```

## Setup

[//]: # (snippet: setup)
```csharp
services.AddMsgHatch()
    .UseResend();
```

`UseResend` selects the Resend client, the email transport, and a named HTTP client, which the host can configure further. It binds `ResendOptions` from the section `MsgHatch:Resend`, and validates them at the start of the host. A value from code wins over the configuration:

[//]: # (snippet: setup-code)
```csharp
services.AddMsgHatch()
    .UseResend(o => o.SenderName = "Example Shop");
```

## Options

[//]: # (options: ResendOptions)
| Option | Type | Default | Rule |
|---|---|---|---|
| `ApiKey` | `string` | none | Required. An API key with the permission "sending access" is enough. Keep it in a secret store, not in `appsettings.json`. |
| `SenderEmail` | `string` | none | Required. The sender address of each email with no address of its own. Its domain must be verified in Resend. |
| `SenderName` | `string?` | none | At most 100 characters. |

[//]: # (snippet: options)
```json
{
  "MsgHatch": {
    "Resend": {
      "SenderEmail": "noreply@example.com",
      "SenderName": "Example Shop"
    }
  }
}
```

The example has no `ApiKey`, because the key comes from a secret store, for example the user secrets or an environment variable `MsgHatch__Resend__ApiKey`.

## Version history

### 0.1.0 (2026-11-22)

- The first version: the Resend email client with `UseResend`, with an idempotency key for each send.
