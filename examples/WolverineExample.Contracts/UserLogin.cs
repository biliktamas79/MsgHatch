namespace WolverineExample.Contracts;

/// <summary>A user signed in.</summary>
/// <param name="TenantId">The tenant of the user.</param>
/// <param name="UserId">The user.</param>
/// <param name="LoggedInAtUtc">The time of the sign-in.</param>
public sealed record UserLogin(string TenantId, int UserId, DateTimeOffset LoggedInAtUtc);
