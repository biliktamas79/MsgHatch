namespace WolverineExample.Consumer.Recipients;

/// <summary>One row of the table <c>Users</c> of <see cref="ConsumerDirectory"/>.</summary>
/// <param name="Id">The id of the user.</param>
/// <param name="CompanyId">The company of the user, or null.</param>
public sealed record ExampleUser(int Id, int? CompanyId);
