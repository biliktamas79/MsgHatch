namespace WolverineExample.Contracts;

/// <summary>A project was finished.</summary>
/// <param name="TenantId">The tenant of the project.</param>
/// <param name="ProjectId">The project.</param>
/// <param name="ProjectName">The name of the project.</param>
/// <param name="OwnerUserId">The user who owns the project.</param>
public sealed record ProjectFinished(string TenantId, int ProjectId, string ProjectName, int OwnerUserId);
