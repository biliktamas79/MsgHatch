using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MsgHatch.Domain.Identity;

namespace MsgHatch.Subscriptions.DependencyInjection;

/// <summary>
/// The logging wrappers of the subscriptions package. <c>AddTenantService</c> adds them only with
/// <c>MsgHatchOptions.EnableLogging</c> (D35).
/// </summary>
internal static class SubscriptionLoggers
{
    public static ISubscriptionStore Wrap(IServiceProvider services, TenantKey key, ISubscriptionStore store) =>
        new SubscriptionStoreLoggingWrapper(store, key, For<SubscriptionStoreLoggingWrapper>(services), services.GetRequiredService<TimeProvider>());

    public static IRecipientDirectory Wrap(IServiceProvider services, TenantKey key, IRecipientDirectory directory) =>
        new RecipientDirectoryLoggingWrapper(
            directory, key, For<RecipientDirectoryLoggingWrapper>(services), services.GetRequiredService<TimeProvider>());

    public static ILogger<T> For<T>(IServiceProvider services) => services.GetService<ILogger<T>>() ?? NullLogger<T>.Instance;
}
