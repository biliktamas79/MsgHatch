using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Management;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using NSubstitute;

namespace MsgHatch.Domain.Tests.Unit.Management;

public sealed partial class DeliveryManagerUnitTests
{
    /// <summary>Each validation rule of section 4.19, with three rows at each limit: a valid row calls a store, an invalid row calls none.</summary>
    public static TheoryData<string, bool> ValidationRows => new()
    {
        { "limit 0", false },
        { "limit 1", true },
        { "limit 500", true },
        { "limit 501", false },
        { "ids 0", false },
        { "ids 1", true },
        { "ids 1000", true },
        { "ids 1001", false },
        { "created from at created to", false },
        { "created from before created to", true },
        { "note 500", true },
        { "note 501", false },
        { "outbox retry with only limit and include test", false },
        { "outbox cancel with only limit and include test", false },
        { "inbox retry with only limit and include test", false },
        { "inbox cancel with only limit and include test", false },
        { "purge max count 0", false },
        { "purge max count 1", true },
        { "purge before now", true },
        { "purge after now", false },
        { "pause of a tenant", false },
        { "resume of a tenant", false },
        { "delete test data of a tenant", false },
        { "purge of a tenant", false },
    };

    [Theory]
    [MemberData(nameof(ValidationRows))]
    public async Task Request_ValidationRule_GivesValidationFailedAndCallsNoStoreOnlyWhenInvalid(string row, bool valid)
    {
        // Act
        var error = await CallAsync(row);

        // Assert
        Assert.Equal((!valid, valid), (error?.Type == Errors.ValidationFailed, StoreCalls() > 0));
    }

    private async Task<Error?> CallAsync(string row) => row switch
    {
        "limit 0" => (await Manager().GetOutgoingMessagesAsync(new OutboxQuery(Limit: 0), Ct)).Error,
        "limit 1" => (await Manager().GetOutgoingMessagesAsync(new OutboxQuery(Limit: 1), Ct)).Error,
        "limit 500" => (await Manager().GetOutgoingMessagesAsync(new OutboxQuery(Limit: 500), Ct)).Error,
        "limit 501" => (await Manager().GetOutgoingMessagesAsync(new OutboxQuery(Limit: 501), Ct)).Error,
        "ids 0" => (await Manager().GetReceivedMessagesAsync(new InboxQuery(Ids: Ids(0)), Ct)).Error,
        "ids 1" => (await Manager().GetReceivedMessagesAsync(new InboxQuery(Ids: Ids(1)), Ct)).Error,
        "ids 1000" => (await Manager().GetReceivedMessagesAsync(new InboxQuery(Ids: Ids(1000)), Ct)).Error,
        "ids 1001" => (await Manager().GetReceivedMessagesAsync(new InboxQuery(Ids: Ids(1001)), Ct)).Error,
        "created from at created to" => (await Manager().GetTrafficAsync(new TrafficQuery(Now, Now), Ct)).Error,
        "created from before created to" => (await Manager().GetTrafficAsync(new TrafficQuery(Now.AddTicks(-1), Now), Ct)).Error,
        "note 500" => await Manager().PauseChannelAsync(TestChannel.Id, new string('n', 500), Ct),
        "note 501" => await Manager().PauseChannelAsync(TestChannel.Id, new string('n', 501), Ct),
        "outbox retry with only limit and include test" =>
            (await Manager().RetryOutgoingAsync(new OutboxQuery(IncludeTest: true, Limit: 10), Ct)).Error,
        "outbox cancel with only limit and include test" =>
            (await Manager().CancelOutgoingAsync(new OutboxQuery(IncludeTest: true, Limit: 10), Ct)).Error,
        "inbox retry with only limit and include test" =>
            (await Manager().RetryReceivedAsync(new InboxQuery(IncludeTest: true, Limit: 10), Ct)).Error,
        "inbox cancel with only limit and include test" =>
            (await Manager().CancelReceivedAsync(new InboxQuery(IncludeTest: true, Limit: 10), Ct)).Error,
        "purge max count 0" => (await Manager().PurgeAsync(Now, 0, Ct)).Error,
        "purge max count 1" => (await Manager().PurgeAsync(Now, 1, Ct)).Error,
        "purge before now" => (await Manager().PurgeAsync(Now, 10, Ct)).Error,
        "purge after now" => (await Manager().PurgeAsync(Now.AddTicks(1), 10, Ct)).Error,
        "pause of a tenant" => await Manager(_t1).PauseChannelAsync(TestChannel.Id, note: null, Ct),
        "resume of a tenant" => await Manager(_t1).ResumeChannelAsync(TestChannel.Id, Ct),
        "delete test data of a tenant" => (await Manager(_t1).DeleteTestDataAsync(Ct)).Error,
        _ => (await Manager(_t1).PurgeAsync(Now, 10, Ct)).Error,
    };

    private int StoreCalls() =>
        _outbox.ReceivedCalls().Count() + _inbox.ReceivedCalls().Count() + _outboxDispatch.ReceivedCalls().Count()
        + _inboxDispatch.ReceivedCalls().Count();

    private static Guid[] Ids(int count) => [.. Enumerable.Range(0, count).Select(static _ => Guid.NewGuid())];
}
