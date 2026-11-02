using MsgHatch.Domain.Identity;

namespace MsgHatch.Domain.Tests.Unit.TestTypes;

/// <summary>A tenant service of a package, for the tests of <c>AddTenantService</c>. It keeps its key, and its name tells a logging wrapper from the inner service.</summary>
public sealed record KeyCapturingService(TenantKey Key, string Name = "inner");
