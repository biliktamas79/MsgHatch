using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Outbox;

namespace MsgHatch.Domain.Tests.Unit.TestTypes;

/// <summary>An outbox dispatch store for the tests of the generic setup method. It deletes nothing.</summary>
public sealed class TestOutboxDispatchStore : IOutboxDispatchStore
{
    public ValueTask<ErrorOr<int>> DeleteTestDataAsync(CancellationToken ct) => ValueTask.FromResult(ErrorOr<int>.ForValue(0));
}
