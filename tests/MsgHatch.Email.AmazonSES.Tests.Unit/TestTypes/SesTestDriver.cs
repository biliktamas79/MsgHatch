using Amazon.SimpleEmailV2;
using Amazon.SimpleEmailV2.Model;
using MsgHatch.Domain.Failures;
using MsgHatch.Email.Tests.Unit.Contracts;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace MsgHatch.Email.AmazonSES.Tests.Unit.TestTypes;

/// <summary>The driver of the SES client tests: an <see cref="IAmazonSimpleEmailServiceV2"/> substitute as the provider.</summary>
public sealed class SesTestDriver : IEmailClientTestDriver
{
    public IAmazonSimpleEmailServiceV2 Ses { get; } = Substitute.For<IAmazonSimpleEmailServiceV2>();

    public void Accepts(string providerMessageId) =>
        Ses.SendEmailAsync(default!, default).ReturnsForAnyArgs(Task.FromResult(new SendEmailResponse { MessageId = providerMessageId }));

    public void Fails(ErrorType type) => Ses.SendEmailAsync(default!, default).ThrowsAsyncForAnyArgs(ExceptionOf(type));

    public void WaitsUntilCancelled() =>
        Ses.SendEmailAsync(default!, default).ReturnsForAnyArgs(static call => WaitUntilCancelledAsync(call.ArgAt<CancellationToken>(1)));

    public void CanSend() =>
        Ses.GetAccountAsync(default!, default).ReturnsForAnyArgs(Task.FromResult(new GetAccountResponse { SendingEnabled = true }));

    public void CannotSend() =>
        Ses.GetAccountAsync(default!, default).ReturnsForAnyArgs(Task.FromResult(new GetAccountResponse { SendingEnabled = false }));

    private static Exception ExceptionOf(ErrorType type) =>
        type == Errors.Rejected ? new MessageRejectedException("The address client1@example.com is not verified.")
        : type == Errors.Unavailable ? new HttpRequestException("No connection.")
        : throw new ArgumentOutOfRangeException(nameof(type), type.Name, "The driver has no SES exception for this error type.");

    private static async Task<SendEmailResponse> WaitUntilCancelledAsync(CancellationToken ct)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, TimeProvider.System, ct);
        return new SendEmailResponse();
    }
}
