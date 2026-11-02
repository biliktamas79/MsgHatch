using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Tests.Unit.Contracts;
using NSubstitute;

namespace MsgHatch.Email.Tests.Unit.TestTypes;

/// <summary>The driver of the email transport tests: an <see cref="IEmailClient"/> substitute as the provider.</summary>
public sealed class EmailTransportTestDriver : ITransportTestDriver
{
    public IEmailClient Client { get; } = Substitute.For<IEmailClient>();

    public OutgoingMessage AMessage() => TestMessages.AnOutgoingEmail();

    public PublishReceipt Succeeds()
    {
        var receipt = new PublishReceipt("ses-1");
        Client.SendAsync(default, default!, default).ReturnsForAnyArgs(ValueTask.FromResult<ErrorOr<PublishReceipt>>(receipt));
        return receipt;
    }

    public void Fails(ErrorType type) =>
        Client.SendAsync(default, default!, default).ReturnsForAnyArgs(ValueTask.FromResult<ErrorOr<PublishReceipt>>(new Error(type)));

    public void WaitsUntilCancelled() =>
        Client.SendAsync(default, default!, default)
            .ReturnsForAnyArgs(static call => new ValueTask<ErrorOr<PublishReceipt>>(WaitUntilCancelledAsync(call.ArgAt<CancellationToken>(2))));

    private static async Task<ErrorOr<PublishReceipt>> WaitUntilCancelledAsync(CancellationToken ct)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, TimeProvider.System, ct).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        return new Error(Errors.OperationCancelled);
    }
}
