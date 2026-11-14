using WolverineExample.Consumer.Recipients;

namespace WolverineExample.Consumer.Startup;

/// <summary>One tenant of <see cref="ConsumerScenario"/>, with its companies and its users.</summary>
/// <param name="Id">The tenant id.</param>
/// <param name="Companies">The companies of the tenant.</param>
/// <param name="Users">The users of the tenant.</param>
public sealed record ScenarioTenant(string Id, IReadOnlyList<ExampleCompany> Companies, IReadOnlyList<ExampleUser> Users);
