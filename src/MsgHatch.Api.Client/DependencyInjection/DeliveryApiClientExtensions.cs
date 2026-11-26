using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Management;

namespace MsgHatch.Api.Client.DependencyInjection;

/// <summary>
/// The setup methods of the HTTP client of the delivery API (D294, section 4.23). Each method selects the HTTP client in place
/// of the in-process defaults of <see cref="IDeliveryQueries"/> and <see cref="IDeliveryCommands"/>, as scoped tenant services
/// with their logging wrappers, through <see cref="MsgHatchBuilder.UseDeliveryQueries"/> and
/// <see cref="MsgHatchBuilder.UseDeliveryCommands"/>. So a second selection gives a setup error. The options bind from the
/// section <c>DeliveryApiClient</c> under the root of <c>AddMsgHatch</c>, by default <c>MsgHatch:DeliveryApiClient</c>,
/// a value from code wins, and an options error stops the start of the host. The host adds its own handlers, for
/// example the authentication, with <c>configureHttp</c>.
/// </summary>
public static class DeliveryApiClientExtensions
{
    private const string _client = "MsgHatch.DeliveryApi";

    /// <summary>Selects the HTTP client for <see cref="IDeliveryQueries"/> and <see cref="IDeliveryCommands"/>.</summary>
    /// <param name="builder">The builder of the setup.</param>
    /// <param name="configure">An optional change of the options in code.</param>
    /// <param name="configureHttp">An optional change of the HTTP client, for example a handler that adds a token.</param>
    /// <returns>The builder.</returns>
    public static MsgHatchBuilder UseDeliveryApiClient(
        this MsgHatchBuilder builder, Action<DeliveryApiClientOptions>? configure = null, Action<IHttpClientBuilder>? configureHttp = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var options = builder.Services.AddOptions<DeliveryApiClientOptions>().BindConfiguration(builder.SectionPathOf(DeliveryApiClientOptions.SectionKey));
        if (configure is not null)
        {
            options.Configure(configure);
        }

        options.ValidateOnStart();
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<DeliveryApiClientOptions>, DeliveryApiClientOptionsValidator>());
        var http = builder.Services.AddHttpClient(_client, static (services, client) =>
        {
            var settings = services.GetRequiredService<IOptions<DeliveryApiClientOptions>>().Value;
            client.BaseAddress = RegistryApiClientExtensions.WithTrailingSlash(settings.BaseAddress!);
            client.Timeout = settings.Timeout;
        });
        configureHttp?.Invoke(http);
        return builder
            .UseDeliveryQueries(static (services, key) => new HttpDeliveryQueries(ApiOf(services), key), nameof(UseDeliveryApiClient))
            .UseDeliveryCommands(static (services, key) => new HttpDeliveryCommands(ApiOf(services), key), nameof(UseDeliveryApiClient));
    }

    /// <summary>Selects the HTTP client of the API at a base address for <see cref="IDeliveryQueries"/> and <see cref="IDeliveryCommands"/>.</summary>
    /// <param name="builder">The builder of the setup.</param>
    /// <param name="baseAddress">The absolute URI of the host of the API.</param>
    /// <param name="configureHttp">An optional change of the HTTP client, for example a handler that adds a token.</param>
    /// <returns>The builder.</returns>
    public static MsgHatchBuilder UseDeliveryApiClient(this MsgHatchBuilder builder, Uri baseAddress, Action<IHttpClientBuilder>? configureHttp = null)
    {
        ArgumentNullException.ThrowIfNull(baseAddress);
        return builder.UseDeliveryApiClient(options => options.BaseAddress = baseAddress, configureHttp);
    }

    private static DeliveryApi ApiOf(IServiceProvider services) =>
        new(services.GetRequiredService<IHttpClientFactory>().CreateClient(_client), services.GetRequiredService<IOptions<DeliveryApiClientOptions>>().Value.Prefix);
}
