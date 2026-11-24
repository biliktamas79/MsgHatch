using Microsoft.EntityFrameworkCore;

namespace MsgHatch.Data.EFCore.DependencyInjection;

/// <summary>
/// The options type of the startup check of an EF Core setup method on <typeparamref name="TContext"/> (section 4.7,
/// D175, D182, D318).
/// </summary>
/// <typeparam name="TContext">The context of the setup method.</typeparam>
internal sealed class EntityFrameworkSetupCheck<TContext>
    where TContext : DbContext
{
    /// <summary>The setup method that the message of a failure names.</summary>
    public string SetupMethod { get; set; } = "";
}
