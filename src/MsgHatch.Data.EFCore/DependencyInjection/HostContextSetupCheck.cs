using Microsoft.EntityFrameworkCore;

namespace MsgHatch.Data.EFCore.DependencyInjection;

/// <summary>
/// The options type of the startup check of the inbox in the transaction of the host on <typeparamref name="TContext"/>
/// (section 4.7, D331).
/// </summary>
/// <typeparam name="TContext">The context of the host.</typeparam>
internal sealed class HostContextSetupCheck<TContext>
    where TContext : DbContext
{
    /// <summary>The setup method that the message of a failure names.</summary>
    public string SetupMethod { get; set; } = "";
}
