using Amazon;
using Amazon.Runtime;
using Amazon.SimpleEmailV2;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Email.DependencyInjection;

namespace MsgHatch.Email.AmazonSES.DependencyInjection;

/// <summary>The setup method of the Amazon SES package (section 4.7, D60).</summary>
public static class AmazonSesExtensions
{
    /// <summary>
    /// Selects Amazon SES for the email channel: the options, <see cref="AmazonSimpleEmailServiceClient"/> as the
    /// <see cref="IEmailClient"/> with its logging wrapper, the email transport, and an
    /// <see cref="IAmazonSimpleEmailServiceV2"/> client for <see cref="AmazonSesOptions.Region"/>, if the host has not
    /// registered one. The options bind from the section <c>MsgHatch:AmazonSes</c>, and a value from code wins. The AWS
    /// SDK finds the credentials.
    /// </summary>
    /// <param name="builder">The builder of the setup.</param>
    /// <param name="configure">An optional change of the options in code.</param>
    /// <returns>The builder.</returns>
    public static MsgHatchBuilder UseAmazonSes(this MsgHatchBuilder builder, Action<AmazonSesOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var options = builder.Services.AddOptions<AmazonSesOptions>().BindConfiguration(AmazonSesOptions.SectionName);
        if (configure is not null)
        {
            options.Configure(configure);
        }

        options.ValidateOnStart();
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<AmazonSesOptions>, AmazonSesOptionsValidator>());
        builder.Services.TryAddSingleton<IAmazonSimpleEmailServiceV2>(
            static services => SesClientOf(services.GetRequiredService<IOptions<AmazonSesOptions>>().Value.Region));
        return builder.UseEmailClient<AmazonSimpleEmailServiceClient>();
    }

    /// <summary>Selects Amazon SES for the email channel, with the sender address from code.</summary>
    /// <param name="builder">The builder of the setup.</param>
    /// <param name="senderEmail">The sender address, the one required option of the package.</param>
    /// <returns>The builder.</returns>
    public static MsgHatchBuilder UseAmazonSes(this MsgHatchBuilder builder, string senderEmail) =>
        builder.UseAmazonSes(o => o.SenderEmail = senderEmail);

    private static AmazonSimpleEmailServiceV2Client SesClientOf(string? region) =>
        region is null ? SesClientOfTheRegionOfTheAwsSdk() : new AmazonSimpleEmailServiceV2Client(RegionEndpoint.GetBySystemName(region));

    private static AmazonSimpleEmailServiceV2Client SesClientOfTheRegionOfTheAwsSdk()
    {
        try
        {
            return new AmazonSimpleEmailServiceV2Client();
        }
        catch (AmazonClientException)
        {
            throw new OptionsValidationException(
                nameof(AmazonSesOptions), typeof(AmazonSesOptions),
                [$"{nameof(AmazonSesOptions.Region)}: the AWS SDK configuration has no region. Set {AmazonSesOptions.SectionName}:{nameof(AmazonSesOptions.Region)}, or a region of the AWS SDK, for example the environment variable AWS_REGION."]);
        }
    }
}
