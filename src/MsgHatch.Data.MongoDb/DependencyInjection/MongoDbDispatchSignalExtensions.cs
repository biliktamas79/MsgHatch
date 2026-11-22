using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Dispatch;

namespace MsgHatch.Data.MongoDb.DependencyInjection;

/// <summary>The setup method of the MongoDB dispatch signal (section 4.7, D303).</summary>
public static class MongoDbDispatchSignalExtensions
{
    /// <summary>
    /// Selects <see cref="MongoDbDispatchSignal"/> on the client and the database of the MongoDB stores (D303). Each host
    /// of the system calls it on the same database, also on another machine. The startup validation fails when the host
    /// does not select the MongoDB stores with <see cref="MongoDbStoreExtensions.UseMongoDbStores"/>, and when
    /// <see cref="DispatchOptions.EnablePolling"/> is false for the inbox or the outbox, because a change while the watch
    /// does not work reaches a loop only through its poll (D190).
    /// </summary>
    /// <param name="builder">The builder of the setup.</param>
    /// <returns>The builder.</returns>
    public static MsgHatchBuilder UseMongoDbDispatchSignal(this MsgHatchBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddOptions<MongoDbDispatchSignalSetupCheck>()
            .Validate<IServiceProviderIsService>(
                static (_, services) => services.IsService(typeof(MongoDbInboxStore)),
                "UseMongoDbDispatchSignal needs the MongoDB stores of UseMongoDbStores, because the signal watches the inserts into their collections.")
            .ValidateOnStart();
        RequirePolling(builder.Services, "Inbox");
        RequirePolling(builder.Services, "Outbox");
        return builder.UseDispatchSignal(
            static services => new MongoDbDispatchSignal(
                MongoDbStoreExtensions.DatabaseOf(services),
                services.GetRequiredService<TimeProvider>(),
                services.GetService<ILogger<MongoDbDispatchSignal>>() ?? NullLogger<MongoDbDispatchSignal>.Instance),
            nameof(UseMongoDbDispatchSignal));
    }

    private static void RequirePolling(IServiceCollection services, string dispatcher) =>
        services.AddOptions<DispatchOptions>(dispatcher)
            .Validate(
                static options => options.EnablePolling,
                $"UseMongoDbDispatchSignal needs EnablePolling true in the dispatch options {dispatcher}, because a change while the watch does not work reaches a loop only through its poll (D190).")
            .ValidateOnStart();
}
