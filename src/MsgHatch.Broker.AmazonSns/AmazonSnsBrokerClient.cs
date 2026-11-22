using System.Collections.Frozen;
using System.Text.Json;
using System.Text.RegularExpressions;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using Microsoft.Extensions.Options;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;

namespace MsgHatch.Broker.AmazonSns;

/// <summary>
/// The Amazon SNS implementation of <see cref="IBrokerClient"/> (D300). Each publish is one <c>Publish</c> call to the
/// topic of the content type of the message: the payload JSON is the message, and each header is a <c>String</c>
/// message attribute. The message id of SNS is the provider message id of the receipt. The client converts each
/// exception of the AWS SDK into an <see cref="Error"/>, and does not throw. An error message names only the exception
/// type, because the text of an SNS exception can contain a header value.
/// </summary>
/// <remarks>
/// The client checks the headers before the call, because SNS sends at most 10 message attributes to an SQS
/// subscription with raw message delivery, and discards a message with more attributes there with no error to the
/// sender (D301). A header name or value that SNS does not accept gives <see cref="Errors.ValidationFailed"/> too.
/// </remarks>
/// <param name="sns">The SNS client of the AWS SDK.</param>
/// <param name="options">The settings.</param>
public sealed partial class AmazonSnsBrokerClient(IAmazonSimpleNotificationService sns, IOptions<AmazonSnsOptions> options) : IBrokerClient
{
    /// <summary>The most headers of one message (D301).</summary>
    public const int MaxHeaders = 10;

    private const string _string = "String";

    private FrozenDictionary<string, string>? _topicsOfTheContentTypes;

    /// <inheritdoc />
    public async ValueTask<ErrorOr<PublishReceipt>> PublishAsync(BrokerPublish publish, CancellationToken ct)
    {
        if (TopicOf(publish.ContentType) is not { } topic)
        {
            return new Error(Errors.NotFound, $"Amazon SNS: no topic for the content type {publish.ContentType}, and no default topic.");
        }

        if (HeaderFailureOf(publish.Headers) is { } failure)
        {
            return new Error(Errors.ValidationFailed, $"Amazon SNS: {failure}");
        }

        try
        {
            var response = await sns.PublishAsync(RequestOf(topic, publish), ct);
            return new PublishReceipt(response.MessageId);
        }
        catch (Exception exception)
        {
            return ErrorOf(exception, ct);
        }
    }

    /// <inheritdoc />
    public ValueTask<Error?> CheckHealthAsync(CancellationToken ct) => ValueTask.FromResult<Error?>(null);

    private string? TopicOf(string contentType) =>
        (_topicsOfTheContentTypes ??= TopicsOfTheContentTypes(options.Value)).TryGetValue(contentType, out var topic)
            ? topic
            : options.Value.DefaultTopicArn;

    private static FrozenDictionary<string, string> TopicsOfTheContentTypes(AmazonSnsOptions options) =>
        options.Topics.ToFrozenDictionary(
            static topic => $"{BrokerChannel.Id.Name}:{topic.Key}", static topic => topic.Value, StringComparer.Ordinal);

    private static string? HeaderFailureOf(IReadOnlyDictionary<string, string> headers)
    {
        if (headers.Count > MaxHeaders)
        {
            return $"the message has {headers.Count} headers, and SNS accepts at most {MaxHeaders} message attributes.";
        }

        foreach (var (name, value) in headers)
        {
            if (!AttributeName().IsMatch(name))
            {
                return $"SNS does not accept the header name {name} as a message attribute name.";
            }

            if (!IsAttributeValue(value))
            {
                return $"SNS does not accept the value of the header {name}: the value is empty or contains a surrogate.";
            }
        }

        return null;
    }

    private static bool IsAttributeValue(string? value) =>
        !string.IsNullOrEmpty(value) && value.AsSpan().IndexOfAnyInRange('\uD800', '\uDFFF') < 0;

    private static PublishRequest RequestOf(string topic, BrokerPublish publish) => new()
    {
        TopicArn = topic,
        Message = JsonSerializer.Serialize(publish.Payload, publish.PayloadJson),
        MessageAttributes = publish.Headers.ToDictionary(
            static header => header.Key, static header => new MessageAttributeValue { DataType = _string, StringValue = header.Value },
            StringComparer.Ordinal),
    };

    private static Error ErrorOf(Exception exception, CancellationToken ct) => exception switch
    {
        OperationCanceledException when ct.IsCancellationRequested => new Error(Errors.OperationCancelled),
        InvalidParameterException or InvalidParameterValueException => SnsError(Errors.Rejected, exception),
        HttpRequestException => SnsError(Errors.Unavailable, exception),
        _ => SnsError(Errors.Exception, exception),
    };

    private static Error SnsError(ErrorType type, Exception exception) =>
        new(type, $"Amazon SNS: {exception.GetType().Name}", exception);

    [GeneratedRegex(@"^(?!(?i:aws|amazon)\.)(?!\.)(?!.*\.\.)[A-Za-z0-9_.-]{1,256}(?<!\.)$")]
    private static partial Regex AttributeName();
}
