using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Management;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using NSubstitute;

namespace MsgHatch.Domain.Tests.Unit.Management;

public sealed partial class DeliveryManagerUnitTests
{
    private static readonly Error _storeError = new(Errors.Unavailable);

    public static TheoryData<string> StoreErrorRows =>
    [
        "down states of the channels",
        "counts of the channels",
        "resume",
        "test data of the inbox",
        "query of a retry",
        "retry of a page",
        "cancel of a page",
    ];

    [Theory]
    [MemberData(nameof(StoreErrorRows))]
    public async Task Action_StoreError_ReturnsTheSameError(string row)
    {
        // Act
        var error = await CallWithAStoreErrorAsync(row);

        // Assert
        Assert.Same(_storeError, error);
    }

    private async Task<Error?> CallWithAStoreErrorAsync(string row)
    {
        var failed = new OutboxQuery(Status: DeliveryStatus.Failed);
        _outboxDispatch.GetDownChannelsAsync(Ct).Returns(ValueTask.FromResult(ErrorOr<IReadOnlyList<ChannelDown>>.ForValue([])));
        _outbox.QueryAsync(default!, Ct).ReturnsForAnyArgs(Page(null, ASummary()));
        return row switch
        {
            "down states of the channels" => await FailAsync(
                () => _outboxDispatch.GetDownChannelsAsync(Ct).Returns(ValueTask.FromResult(ErrorOr<IReadOnlyList<ChannelDown>>.ForError(_storeError))),
                async () => (await Manager().GetChannelsAsync(Ct)).Error),
            "counts of the channels" => await FailAsync(
                () => _outboxDispatch.CountAsync(default!, Ct).ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<IReadOnlyList<MessageCount>>.ForError(_storeError))),
                async () => (await Manager().GetChannelsAsync(Ct)).Error),
            "resume" => await FailAsync(
                () => _outboxDispatch.SetChannelUpAsync(default, default, Ct).ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<bool>.ForError(_storeError))),
                async () => await Manager().ResumeChannelAsync(TestChannel.Id, Ct)),
            "test data of the inbox" => await FailAsync(
                () => _inboxDispatch.DeleteTestDataAsync(Ct).Returns(ValueTask.FromResult(ErrorOr<int>.ForError(_storeError))),
                async () => (await Manager().DeleteTestDataAsync(Ct)).Error),
            "query of a retry" => await FailAsync(
                () => _outbox.QueryAsync(default!, Ct).ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<MessagePage<OutgoingMessageSummary>>.ForError(_storeError))),
                async () => (await Manager().RetryOutgoingAsync(failed, Ct)).Error),
            "retry of a page" => await FailAsync(
                () => _outbox.RetryAsync(default!, default, default, Ct).ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<int>.ForError(_storeError))),
                async () => (await Manager().RetryOutgoingAsync(failed, Ct)).Error),
            _ => await FailAsync(
                () => _outbox.CancelAsync(default!, default, Ct).ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<int>.ForError(_storeError))),
                async () => (await Manager().CancelOutgoingAsync(failed, Ct)).Error),
        };
    }

    private static async Task<Error?> FailAsync(Action configure, Func<Task<Error?>> call)
    {
        configure();
        return await call();
    }
}
