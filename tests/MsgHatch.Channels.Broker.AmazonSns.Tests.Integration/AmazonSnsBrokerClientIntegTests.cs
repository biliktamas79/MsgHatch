using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Channels.Broker.AmazonSns.DependencyInjection;
using MsgHatch.Channels.Broker.AmazonSns.Tests.Integration.TestTypes;
using MsgHatch.Channels.Broker.AmazonSns.Tests.Unit.TestTypes;
using MsgHatch.Channels.Broker.Tests.Unit.TestTypes;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Tests.Unit.TestTypes;

[assembly: AssemblyFixture(typeof(NoAwsCredentials))]

namespace MsgHatch.Channels.Broker.AmazonSns.Tests.Integration;

/// <summary>
/// The Amazon SNS broker client against a real SNS topic, through <c>UseAmazonSnsBroker</c> (D300 to D302). The topic and
/// the SQS queue of its raw message delivery subscription come from <see cref="SnsAccount"/>, and each test is skipped
/// while the settings are not complete. A test deletes each message that it receives from the queue.
/// </summary>
public sealed class AmazonSnsBrokerClientIntegTests
{
    private const int _receiveTries = 6;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact(Skip = SnsAccount.NotConfigured, SkipUnless = nameof(SnsAccount.IsConfigured), SkipType = typeof(SnsAccount))]
    public async Task PublishAsync_MessageWithHeaders_ArrivesInTheQueueWithThePayloadJsonAndEachHeaderAsAMessageAttribute()
    {
        // Arrange
        await using var provider = HostWith(SnsAccount.Settings.TopicArn!);
        using var sqs = SnsAccount.NewSqsClient();
        var id = Guid.NewGuid().ToString();

        // Act
        var result = await provider.GetRequiredService<IBrokerClient>().PublishAsync(APublish(id), Ct);

        // Assert
        Assert.NotEmpty(result.Value.ProviderMessageId!);
        var message = await ReceiveAsync(sqs, id);
        Assert.Equal("""{"text":"order 1001"}""", message.Body);
        Assert.Equal(
            [("msghatch-id", "String", id), ("x-source", "String", "orders")],
            message.MessageAttributes
                .Select(static attribute => (attribute.Key, attribute.Value.DataType, attribute.Value.StringValue))
                .OrderBy(static attribute => attribute.Key, StringComparer.Ordinal));
    }

    [Fact(Skip = SnsAccount.NotConfigured, SkipUnless = nameof(SnsAccount.IsConfigured), SkipType = typeof(SnsAccount))]
    public async Task CheckHealthAsync_TopicOfTheSettings_ReturnsNull()
    {
        // Arrange
        await using var provider = HostWith(SnsAccount.Settings.TopicArn!);

        // Act
        var error = await provider.GetRequiredService<IBrokerClient>().CheckHealthAsync(Ct);

        // Assert
        Assert.Null(error);
    }

    [Fact(Skip = SnsAccount.NotConfigured, SkipUnless = nameof(SnsAccount.IsConfigured), SkipType = typeof(SnsAccount))]
    public async Task CheckHealthAsync_TopicThatDoesNotExist_ReturnsUnavailable()
    {
        // Arrange
        await using var provider = HostWith(SnsAccount.MissingTopicArn);

        // Act
        var error = await provider.GetRequiredService<IBrokerClient>().CheckHealthAsync(Ct);

        // Assert
        Assert.Equal(Errors.Unavailable, error?.Type);
    }

    private static ServiceProvider HostWith(string topicArn)
    {
        var services = TestHost.CreateServices();
        services.AddSingleton(static _ => SnsAccount.NewSnsClient());
        services.AddMsgHatch().UseAmazonSnsBroker(o =>
        {
            o.Region = SnsAccount.Settings.Region;
            o.DefaultTopicArn = topicArn;
        });
        return services.Build();
    }

    private static BrokerPublish APublish(string id) =>
        new(
            Guid.NewGuid(), Tenant: null, BrokerMessages.ContentType, new TestPayload("order 1001"), BrokerTestJsonContext.Default.TestPayload,
            new Dictionary<string, string> { ["msghatch-id"] = id, ["x-source"] = "orders" });

    private static async Task<Message> ReceiveAsync(IAmazonSQS sqs, string id)
    {
        for (var tryNumber = 0; tryNumber < _receiveTries; tryNumber++)
        {
            var response = await sqs.ReceiveMessageAsync(
                new ReceiveMessageRequest { QueueUrl = SnsAccount.Settings.QueueUrl, MaxNumberOfMessages = 10, WaitTimeSeconds = 5, MessageAttributeNames = ["All"] },
                Ct);
            Message? match = null;
            foreach (var message in response.Messages ?? [])
            {
                await sqs.DeleteMessageAsync(SnsAccount.Settings.QueueUrl, message.ReceiptHandle, Ct);
                match ??= IdOf(message) == id ? message : null;
            }

            if (match is not null)
            {
                return match;
            }
        }

        throw new TimeoutException($"The queue has no message with the id {id} after {_receiveTries} receives.");
    }

    private static string? IdOf(Message message) =>
        message.MessageAttributes?.TryGetValue("msghatch-id", out var value) == true ? value.StringValue : null;
}
