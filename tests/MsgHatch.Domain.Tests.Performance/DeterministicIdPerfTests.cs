using BenchmarkDotNet.Attributes;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Routing;

namespace MsgHatch.Domain.Tests.Performance;

/// <summary>The id of one outgoing message (section 4.11): it must allocate nothing.</summary>
[MemoryDiagnoser]
public class DeterministicIdPerfTests
{
    private readonly Guid _messageId = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e");
    private readonly TenantId _tenant = new("tenant-1");
    private readonly DeliveryTarget _target = new(DeliveryTargetKind.User, "client-42");

    [Benchmark]
    public Guid Create() => DeterministicId.Create(_messageId, _tenant, _target, PerfChannel.Id);
}
