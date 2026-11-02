using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Email.DependencyInjection;

namespace MsgHatch.Email.Resend.DependencyInjection;

/// <summary>The setup method of the Resend package (section 4.7, D60).</summary>
public static class ResendExtensions
{
    /// <summary>
    /// Selects Resend for the email channel: the options, <see cref="ResendEmailClient"/> as the
    /// <see cref="IEmailClient"/> with its logging wrapper, the email transport, and the HTTP client
    /// <see cref="ResendEmailClient.HttpClientName"/>, which the host can configure further. The options bind from the
    /// section <c>MsgHatch:Resend</c>, and a value from code wins.
    /// </summary>
    /// <param name="builder">The builder of the setup.</param>
    /// <param name="configure">An optional change of the options in code.</param>
    /// <returns>The builder.</returns>
    public static MsgHatchBuilder UseResend(this MsgHatchBuilder builder, Action<ResendOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var options = builder.Services.AddOptions<ResendOptions>().BindConfiguration(ResendOptions.SectionName);
        if (configure is not null)
        {
            options.Configure(configure);
        }

        options.ValidateOnStart();
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<ResendOptions>, ResendOptionsValidator>());
        builder.Services.AddHttpClient(ResendEmailClient.HttpClientName);
        return builder.UseEmailClient<ResendEmailClient>();
    }
}
