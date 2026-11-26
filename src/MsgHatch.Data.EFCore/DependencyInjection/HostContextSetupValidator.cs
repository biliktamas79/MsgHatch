using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MsgHatch.Data.EFCore.Messages;

namespace MsgHatch.Data.EFCore.DependencyInjection;

/// <summary>
/// The startup check of the inbox in the transaction of the host (D331): <typeparamref name="TContext"/> is a scoped
/// service, so that the inbox row and the rows of the host are in one save, its provider is relational, its model has the
/// inbox of <c>MapMsgHatchInbox</c>, and it uses the database of the store contexts, so that the inbox loop reads the row.
/// The check compares the server and the database of the two connections, and opens no connection. The message names the
/// setup method.
/// </summary>
/// <typeparam name="TContext">The context of the host.</typeparam>
/// <param name="scopes">The scopes of the application services.</param>
/// <param name="stores">The pooled factory of the store contexts.</param>
internal sealed class HostContextSetupValidator<TContext>(IServiceScopeFactory scopes, IDbContextFactory<MsgHatchDbContext> stores)
    : IValidateOptions<HostContextSetupCheck<TContext>>
    where TContext : DbContext
{
    private static readonly string _context = typeof(TContext).Name;

    public ValidateOptionsResult Validate(string? name, HostContextSetupCheck<TContext> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        using var first = scopes.CreateScope();
        using var second = scopes.CreateScope();
        if (first.ServiceProvider.GetService<TContext>() is not { } context)
        {
            return ValidateOptionsResult.Fail(
                $"{options.SetupMethod} needs {_context} as a scoped service, for example with AddDbContext<{_context}>, and no {_context} is registered.");
        }

        if (!ReferenceEquals(context, first.ServiceProvider.GetService<TContext>()) || ReferenceEquals(context, second.ServiceProvider.GetService<TContext>()))
        {
            return ValidateOptionsResult.Fail(
                $"{options.SetupMethod} needs {_context} as a scoped service, so that the inbox row and the rows of the host are in one save, and {_context} is not scoped.");
        }

        if (!context.Database.IsRelational())
        {
            return ValidateOptionsResult.Fail(
                $"{options.SetupMethod} needs a relational EF Core provider for {_context}, and {context.Database.ProviderName} is not relational.");
        }

        if (context.Model.FindEntityType(typeof(InboxRow)) is null)
        {
            return ValidateOptionsResult.Fail(
                $"{options.SetupMethod} needs the inbox in the model of {_context}: call modelBuilder.MapMsgHatchInbox(this) in its OnModelCreating.");
        }

        return DatabaseResult(context, options.SetupMethod);
    }

    private ValidateOptionsResult DatabaseResult(TContext context, string setupMethod)
    {
        using var store = stores.CreateDbContext();
        if (!store.Database.IsRelational())
        {
            return ValidateOptionsResult.Success;
        }

        var (host, msgHatch) = (context.Database.GetDbConnection(), store.Database.GetDbConnection());
        return string.Equals(host.DataSource, msgHatch.DataSource, StringComparison.OrdinalIgnoreCase)
            && string.Equals(host.Database, msgHatch.Database, StringComparison.OrdinalIgnoreCase)
                ? ValidateOptionsResult.Success
                : ValidateOptionsResult.Fail(
                    $"{setupMethod} needs {_context} and the store contexts on one database, so that the inbox loop reads the inbox rows of the host, and {_context} uses the database {host.Database} on {host.DataSource}, but the store contexts use {msgHatch.Database} on {msgHatch.DataSource}.");
    }
}
