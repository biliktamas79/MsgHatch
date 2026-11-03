namespace MsgHatch.Broker.Tests.Unit.Contracts;

/// <summary>The rows of the data-driven tests of <see cref="BrokerClientTestsBase{TClient}"/>. A generic class has no static members (CA1000).</summary>
public static class BrokerClientTestRows
{
    public static TheoryData<string, string?> TenantRows => new()
    {
        { "no tenant", null },
        { "a tenant", "t1" },
    };
}
