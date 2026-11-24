using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MsgHatch.Data.EFCore.DependencyInjection;

/// <summary>
/// The startup check of an EF Core setup method (D175, D182, D318): the EF Core provider of
/// <typeparamref name="TContext"/> is relational, and the dialect of a provider package serves that provider. The message
/// names the setup method and the provider. The check opens no connection.
/// </summary>
/// <typeparam name="TContext">The context of the setup method.</typeparam>
/// <param name="contexts">The pooled factory of the contexts.</param>
internal sealed class EntityFrameworkSetupValidator<TContext>(IDbContextFactory<TContext> contexts)
    : IValidateOptions<EntityFrameworkSetupCheck<TContext>>
    where TContext : DbContext
{
    public ValidateOptionsResult Validate(string? name, EntityFrameworkSetupCheck<TContext> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        using var context = contexts.CreateDbContext();
        var provider = context.Database.ProviderName;
        if (!context.Database.IsRelational())
        {
            return ValidateOptionsResult.Fail($"{options.SetupMethod} needs a relational EF Core provider, and {provider} is not relational.");
        }

        return StoreOptionsExtension.Of(context).Dialect is { } dialect && dialect.ProviderName != provider
            ? ValidateOptionsResult.Fail(
                $"{options.SetupMethod} has the dialect of the EF Core provider {dialect.ProviderName}, but the provider of the context is {provider}.")
            : ValidateOptionsResult.Success;
    }
}
