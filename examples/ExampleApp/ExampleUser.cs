namespace ExampleApp;

/// <summary>One row of the table <c>Users</c> of <see cref="ExampleRecipientDirectory"/>.</summary>
/// <param name="Id">The id of the user.</param>
/// <param name="CompanyId">The company of the user, or null.</param>
/// <param name="UiLanguage">The UI language of the user.</param>
/// <param name="Email">The email address of the user, or null.</param>
public sealed record ExampleUser(int Id, int? CompanyId, string UiLanguage, string? Email);
