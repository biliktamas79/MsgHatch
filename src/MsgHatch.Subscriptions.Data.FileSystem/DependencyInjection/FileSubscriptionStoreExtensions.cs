using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Data.FileSystem;
using MsgHatch.Data.FileSystem.DependencyInjection;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Subscriptions.DependencyInjection;

namespace MsgHatch.Subscriptions.Data.FileSystem.DependencyInjection;

/// <summary>The setup method of the JSON file subscription store (section 4.7, D60).</summary>
public static class FileSubscriptionStoreExtensions
{
    /// <summary>
    /// Selects <see cref="FileSystemSubscriptionStore"/> under <c>{RootPath}/subscriptions</c>, with its logging wrapper,
    /// one instance for each tenant key. It shares the <see cref="FileStoreOptions"/> of <c>UseFileStores</c> (D59): the
    /// options bind from the section <c>MsgHatch:FileStores</c>, and a value from code wins. A value from code here and
    /// in <c>UseFileStores</c> fails the startup validation (D150).
    /// </summary>
    /// <param name="builder">The builder of the setup.</param>
    /// <param name="configure">An optional change of the options in code.</param>
    /// <returns>The builder.</returns>
    public static MsgHatchBuilder UseFileSubscriptionStore(this MsgHatchBuilder builder, Action<FileStoreOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        FileStoreExtensions.AddOptions(builder, nameof(UseFileSubscriptionStore), configure);
        return builder.UseSubscriptionStore(
            static (services, key) => new FileSystemSubscriptionStore(
                FileStoreExtensions.RootPathOf(services), key, services.GetRequiredService<ChannelSettingsJson>()),
            nameof(UseFileSubscriptionStore));
    }

    /// <summary>Selects the JSON file subscription store on a folder.</summary>
    /// <param name="builder">The builder of the setup.</param>
    /// <param name="rootPath">The root folder of the file stores.</param>
    /// <returns>The builder.</returns>
    public static MsgHatchBuilder UseFileSubscriptionStore(this MsgHatchBuilder builder, string rootPath) =>
        builder.UseFileSubscriptionStore(o => o.RootPath = rootPath);
}
