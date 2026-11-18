using MsgHatch.Domain.Identity;

namespace MsgHatch.Subscriptions.Tests.Unit.TestTypes;

/// <summary>
/// A started host of one implementation of a management interface, for the test base classes (D231): the implementation
/// of a tenant key, and the services of the host that holds the subscription store, the directory, and the outbox.
/// </summary>
/// <typeparam name="T">The management interface.</typeparam>
public interface IManagementTestHost<out T> : IAsyncDisposable
{
    /// <summary>The services of the host with the stores, in one DI scope.</summary>
    IServiceProvider Server { get; }

    /// <summary>Gives the implementation of one tenant key.</summary>
    /// <param name="key">The tenant key.</param>
    /// <returns>The implementation.</returns>
    T Service(TenantKey key);
}
