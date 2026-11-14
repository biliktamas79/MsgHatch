namespace WolverineExample.Consumer.Recipients;

/// <summary>One row of the table <c>Companies</c> of <see cref="ConsumerDirectory"/>.</summary>
/// <param name="Id">The id of the company.</param>
/// <param name="AdminEmail">The email address of the company administrator, or null.</param>
public sealed record ExampleCompany(int Id, string? AdminEmail);
