using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Tests.Unit.Contracts;

namespace MsgHatch.Domain.Tests.Unit.Dispatch;

public sealed class InProcessDispatchSignalUnitTests() : DispatchSignalTestsBase<InProcessDispatchSignal>(new InProcessDispatchSignal());
