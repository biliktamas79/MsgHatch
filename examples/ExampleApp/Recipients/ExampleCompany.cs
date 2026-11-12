namespace ExampleApp.Recipients;

/// <summary>One row of the table <c>Companies</c> of <see cref="ExampleRecipientDirectory"/>.</summary>
/// <param name="Id">The id of the company.</param>
/// <param name="DefaultLanguage">The default language of the company.</param>
/// <param name="Email">The email address of the company itself, or null.</param>
public sealed record ExampleCompany(int Id, string DefaultLanguage, string? Email);
