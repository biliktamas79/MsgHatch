using MsgHatch.Domain.Identity;

namespace MsgHatch.Domain.Tests.Unit.TestTypes;

/// <summary>
/// A started host of one implementation of the registry queries, for <c>RegistryQueriesTestsBase</c>: the implementation
/// of a tenant key, and the services of the host that holds the registry.
/// </summary>
/// <typeparam name="T">The query interface.</typeparam>
public interface IRegistryQueriesTestHost<out T> : IAsyncDisposable
{
    /// <summary>The services of the host with the registry, in one DI scope.</summary>
    IServiceProvider Server { get; }

    /// <summary>Gives the implementation of one tenant key.</summary>
    /// <param name="key">The tenant key.</param>
    /// <returns>The implementation.</returns>
    T Service(TenantKey key);
}
