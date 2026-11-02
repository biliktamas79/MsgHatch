using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;

namespace MsgHatch.Email.Tests.Unit.TestTypes;

/// <summary>An email client that the DI container creates, for the setup method with a client type.</summary>
public sealed class FixedReceiptEmailClient : IEmailClient
{
    public static readonly PublishReceipt Receipt = new("fixed-1");

    public ValueTask<ErrorOr<PublishReceipt>> SendAsync(Guid messageId, EmailMessage message, CancellationToken ct) =>
        ValueTask.FromResult<ErrorOr<PublishReceipt>>(Receipt);

    public ValueTask<Error?> CheckHealthAsync(CancellationToken ct) => ValueTask.FromResult<Error?>(null);
}
