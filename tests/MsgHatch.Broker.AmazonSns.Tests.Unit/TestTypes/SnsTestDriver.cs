using System.Text.Json;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using MsgHatch.Broker.Tests.Unit.Contracts;
using MsgHatch.Broker.Tests.Unit.TestTypes;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace MsgHatch.Broker.AmazonSns.Tests.Unit.TestTypes;

/// <summary>
/// The driver of the SNS client tests: an <see cref="IAmazonSimpleNotificationService"/> substitute as the library. SNS
/// gets the payload as JSON and the tenant as the header <c>msghatch-tenant</c>, so the driver reads both back from the
/// request.
/// </summary>
public sealed class SnsTestDriver : IBrokerClientTestDriver
{
    public IAmazonSimpleNotificationService Sns { get; } = Substitute.For<IAmazonSimpleNotificationService>();

    public PublishReceipt Accepts()
    {
        Sns.PublishAsync(default(PublishRequest)!, default).ReturnsForAnyArgs(Task.FromResult(new PublishResponse { MessageId = "sns-1" }));
        return new PublishReceipt("sns-1");
    }

    public void Fails(ErrorType type) => Sns.PublishAsync(default(PublishRequest)!, default).ThrowsAsyncForAnyArgs(ExceptionOf(type));

    public void WaitsUntilCancelled() =>
        Sns.PublishAsync(default(PublishRequest)!, default)
            .ReturnsForAnyArgs(static call => WaitUntilCancelledAsync(call.ArgAt<CancellationToken>(1)));

    public (object Payload, IReadOnlyDictionary<string, string?> Headers, string? Tenant) Published()
    {
        var request = SentRequest();
        var headers = request.MessageAttributes.ToDictionary(static a => a.Key, static a => (string?)a.Value.StringValue, StringComparer.Ordinal);
        return (JsonSerializer.Deserialize(request.Message, BrokerTestJsonContext.Default.TestPayload)!, headers, headers.GetValueOrDefault("msghatch-tenant"));
    }

    public PublishRequest SentRequest() => (PublishRequest)Sns.ReceivedCalls().Single().GetArguments()[0]!;

    private static Exception ExceptionOf(ErrorType type) =>
        type == Errors.Rejected ? new InvalidParameterException("Invalid parameter: the value x-secret is not valid.")
        : type == Errors.Unavailable ? new HttpRequestException("No connection.")
        : throw new ArgumentOutOfRangeException(nameof(type), type.Name, "The driver has no SNS exception for this error type.");

    private static async Task<PublishResponse> WaitUntilCancelledAsync(CancellationToken ct)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, TimeProvider.System, ct);
        return new PublishResponse();
    }
}
