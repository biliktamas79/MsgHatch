namespace MsgHatch.Domain.Tests.Unit.TestTypes;

/// <summary>A tenant service that gets another tenant service, for the tests of <c>AddTenantService</c>.</summary>
public sealed record DependentService(KeyCapturingService Inner);
