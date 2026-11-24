# MsgHatch.Broker.AmazonSns

The Amazon SNS client of the [MsgHatch](https://github.com/biliktamas79/MsgHatch) broker channel. The package implements `IBrokerClient` of `MsgHatch.Broker` with the SNS API of the AWS SDK. Each broker message goes to a standard SNS topic.

A receipt means that SNS accepted the message for the topic. SNS delivers to each subscription later, and a subscription that cannot receive loses the message. SNS sends at most 10 message attributes to an SQS subscription with raw message delivery, so the client refuses a message with more than 10 headers.

## Install

```shell
dotnet add package MsgHatch.Broker.AmazonSns
```

## Setup

[//]: # (snippet: setup)
```csharp
services.AddMsgHatch()
    .UseAmazonSnsBroker();
```

`UseAmazonSnsBroker` selects the SNS client and the broker transport. It registers an `IAmazonSimpleNotificationService` client for the region of the options when the host has not registered one, so the AWS SDK gives the credentials. It binds `AmazonSnsOptions` from the section `MsgHatch:AmazonSns`, and validates them at the start of the host. A value from code wins over the configuration:

[//]: # (snippet: setup-code)
```csharp
services.AddMsgHatch()
    .UseAmazonSnsBroker(o => o.Region = "eu-central-1");
```

## Options

[//]: # (options: AmazonSnsOptions)
| Option | Type | Default | Rule |
|---|---|---|---|
| `Region` | `string?` | the region of the AWS SDK configuration | An AWS region, for example `eu-west-1`. |
| `DefaultTopicArn` | `string?` | none | The topic of each broker registration with no topic in `Topics`. A standard topic, not FIFO. |
| `Topics` | `Dictionary<string, string>` | empty | The topic ARN of each broker registration, with the name of the registration as the key: `OrderShipped` for the content type `Broker:OrderShipped`. Each ARN is a standard topic. |

[//]: # (snippet: options)
```json
{
  "MsgHatch": {
    "AmazonSns": {
      "Region": "eu-west-1",
      "DefaultTopicArn": "arn:aws:sns:eu-west-1:123456789012:shop-events",
      "Topics": {
        "OrderShipped": "arn:aws:sns:eu-west-1:123456789012:shop-orders"
      }
    }
  }
}
```

## Version history

### 0.2.1 (2026-11-24)

- The package depends on the latest stable version of `AWSSDK.SimpleNotificationService` within the major 4 at the time of the pack, not on 4.0.100.22. The API does not change.

### 0.1.0 (2026-11-22)

- The first version: the Amazon SNS broker client with `UseAmazonSnsBroker`, a topic for each registration or one default topic, and a rate limit error with the wait that SNS asks for.
