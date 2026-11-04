using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Persistence;

namespace MsgHatch.Data.FileSystem.DependencyInjection;

/// <summary>The setup method of the JSON file stores (section 4.7, D60).</summary>
public static class FileStoreExtensions
{
    /// <summary>
    /// Selects the JSON file stores (D26): <see cref="FileSystemInboxStore"/> under <c>{RootPath}/inbox</c> and
    /// <see cref="FileSystemOutboxStore"/> under <c>{RootPath}/outbox</c>, each with its two parts and their logging
    /// wrappers. The options bind from the section <c>MsgHatch:FileStores</c>, and a value from code wins.
    /// </summary>
    /// <param name="builder">The builder of the setup.</param>
    /// <param name="configure">An optional change of the options in code.</param>
    /// <returns>The builder.</returns>
    public static MsgHatchBuilder UseFileStores(this MsgHatchBuilder builder, Action<FileStoreOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        AddOptions(builder, nameof(UseFileStores), configure);
        builder.Services.TryAddSingleton(static services => new FileSystemInboxStore(RootPathOf(services), services.GetRequiredService<MsgHatchJson>()));
        builder.Services.TryAddSingleton(static services => new FileSystemOutboxStore(RootPathOf(services), services.GetRequiredService<MsgHatchJson>()));
        return builder
            .UseInboxStore(static (services, key) => services.GetRequiredService<FileSystemInboxStore>().For(key), nameof(UseFileStores))
            .UseInboxDispatchStore(static services => services.GetRequiredService<FileSystemInboxStore>(), nameof(UseFileStores))
            .UseOutboxStore(static (services, key) => services.GetRequiredService<FileSystemOutboxStore>().For(key), nameof(UseFileStores))
            .UseOutboxDispatchStore(static services => services.GetRequiredService<FileSystemOutboxStore>(), nameof(UseFileStores));
    }

    /// <summary>Selects the JSON file stores on a folder.</summary>
    /// <param name="builder">The builder of the setup.</param>
    /// <param name="rootPath">The root folder of the stores.</param>
    /// <returns>The builder.</returns>
    public static MsgHatchBuilder UseFileStores(this MsgHatchBuilder builder, string rootPath) =>
        builder.UseFileStores(o => o.RootPath = rootPath);

    /// <summary>
    /// Registers the shared <see cref="FileStoreOptions"/> of a setup method of the file stores (D59): the binding of the
    /// section <c>MsgHatch:FileStores</c>, the validation, and a value in code through <c>ConfigureSharedOptions</c> (D150).
    /// </summary>
    internal static void AddOptions(MsgHatchBuilder builder, string setupMethod, Action<FileStoreOptions>? configure)
    {
        builder.Services.AddOptions<FileStoreOptions>().BindConfiguration(FileStoreOptions.SectionName).ValidateOnStart();
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<FileStoreOptions>, FileStoreOptionsValidator>());
        if (configure is not null)
        {
            builder.ConfigureSharedOptions(setupMethod, configure);
        }
    }

    internal static string RootPathOf(IServiceProvider services) =>
        services.GetRequiredService<IOptions<FileStoreOptions>>().Value.RootPath;
}
