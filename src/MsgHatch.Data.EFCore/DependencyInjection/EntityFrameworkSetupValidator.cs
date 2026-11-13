using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MsgHatch.Data.EFCore.DependencyInjection;

/// <summary>
/// The startup check of <c>UseEntityFrameworkStores</c> (D175): the EF Core provider of <see cref="MsgHatchDbContext"/>
/// has the SQL statements of the stores. The message names the provider. The check opens no connection.
/// </summary>
/// <param name="contexts">The pooled factory of the contexts.</param>
internal sealed class EntityFrameworkSetupValidator(IDbContextFactory<MsgHatchDbContext> contexts) : IValidateOptions<EntityFrameworkSetupCheck>
{
    public ValidateOptionsResult Validate(string? name, EntityFrameworkSetupCheck options)
    {
        using var context = contexts.CreateDbContext();
        var provider = context.Database.ProviderName;
        return StoreModel.HasStatements(provider)
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                $"UseEntityFrameworkStores has no SQL statements for the EF Core provider {provider}. The stores support {StoreModel.SqlServerProvider}.");
    }
}
