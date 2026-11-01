using MsgHatch.Domain.Delivery;

namespace MsgHatch.Domain.Tests.Unit.Contracts;

/// <summary>The rows of the data-driven tests of <see cref="OutboxStoreTestsBase{TStore}"/>. A generic class has no static members (CA1000).</summary>
public static class OutboxStoreTestRows
{
    public static TheoryData<string, string> OtherTenantRows => new()
    {
        { "t1", "t2" },
        { "", "t1" }, // "" is no tenant
        { "t1", "" },
    };

    public static TheoryData<PublishOutcome, bool, DeliveryStatus> OutcomeRows => new()
    {
        { PublishOutcome.Succeeded, false, DeliveryStatus.Done },
        { PublishOutcome.Suppressed, false, DeliveryStatus.Done },
        { PublishOutcome.TransientError, true, DeliveryStatus.Pending },
        { PublishOutcome.TransientError, false, DeliveryStatus.Failed },
        { PublishOutcome.PermanentError, false, DeliveryStatus.Failed },
        { PublishOutcome.Expired, false, DeliveryStatus.Failed },
    };
}
