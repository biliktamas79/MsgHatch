using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MsgHatch.Data.EFCore.Model;

namespace MsgHatch.Data.EFCore.DependencyInjection;

/// <summary>
/// The startup check of an EF Core setup method (D175, D182, D318, D320, D326): the EF Core provider of
/// <typeparamref name="TContext"/> is relational, the dialect of a provider package serves that provider, and the option
/// <see cref="EntityFrameworkStoreOptions.Schema"/> is the default on a database with no schemas. The message
/// names the setup method and the provider. The check opens no connection, except with
/// <see cref="EntityFrameworkStoreOptions.RequireCurrentSchema"/>: then it reads the history table of the migrations, and
/// fails for the first migration that is not applied.
/// </summary>
/// <typeparam name="TContext">The context of the setup method.</typeparam>
/// <param name="contexts">The pooled factory of the contexts.</param>
/// <param name="storeOptions">The options of the stores.</param>
internal sealed class EntityFrameworkSetupValidator<TContext>(IDbContextFactory<TContext> contexts, IOptions<EntityFrameworkStoreOptions> storeOptions)
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

        var contextOptions = StoreOptionsExtension.Of(context);
        if (contextOptions.Dialect is { } dialect && dialect.ProviderName != provider)
        {
            return ValidateOptionsResult.Fail(
                $"{options.SetupMethod} has the dialect of the EF Core provider {dialect.ProviderName}, but the provider of the context is {provider}.");
        }

        if (!contextOptions.HasSchemas && contextOptions.Schema != EntityFrameworkStoreOptions.DefaultSchema)
        {
            return ValidateOptionsResult.Fail(
                $"{options.SetupMethod} has the dialect of {provider}, whose database has no schemas, so the option Schema has no effect and must be {EntityFrameworkStoreOptions.DefaultSchema}, not {contextOptions.Schema}.");
        }

        return storeOptions.Value.RequireCurrentSchema ? CurrentSchemaResult(context, options.SetupMethod) : ValidateOptionsResult.Success;
    }

    private static ValidateOptionsResult CurrentSchemaResult(TContext context, string setupMethod)
    {
        try
        {
            return context.Database.GetPendingMigrations().FirstOrDefault() is { } pending
                ? ValidateOptionsResult.Fail(
                    $"{setupMethod} requires the current schema, but the migration {pending} of {typeof(TContext).Name} is not applied.")
                : ValidateOptionsResult.Success;
        }
        catch (DbException exception)
        {
            return ValidateOptionsResult.Fail(
                $"{setupMethod} requires the current schema, but the history of the migrations of {typeof(TContext).Name} is not readable: {exception.Message}");
        }
    }
}
