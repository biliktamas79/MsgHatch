using Amazon;
using Amazon.Runtime;
using Amazon.SimpleNotificationService;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using MsgHatch.Channels.Broker.DependencyInjection;
using MsgHatch.Domain.DependencyInjection;

namespace MsgHatch.Channels.Broker.AmazonSns.DependencyInjection;

/// <summary>The setup method of the Amazon SNS broker package (section 4.7, D60, D300).</summary>
public static class AmazonSnsBrokerExtensions
{
    /// <summary>
    /// Selects Amazon SNS for the broker channel: the options, <see cref="AmazonSnsBrokerClient"/> as the
    /// <see cref="IBrokerClient"/> with its logging wrapper, the broker transport, and an
    /// <see cref="IAmazonSimpleNotificationService"/> client for <see cref="AmazonSnsOptions.Region"/>, if the host has not
    /// registered one. The options bind from the section <c>AmazonSns</c> under the root of <c>AddMsgHatch</c>, by default
    /// <c>MsgHatch:AmazonSns</c>, and a value from code wins. The AWS SDK finds the credentials.
    /// </summary>
    /// <param name="builder">The builder of the setup.</param>
    /// <param name="configure">An optional change of the options in code.</param>
    /// <returns>The builder.</returns>
    public static MsgHatchBuilder UseAmazonSnsBroker(this MsgHatchBuilder builder, Action<AmazonSnsOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var section = builder.SectionPathOf(AmazonSnsOptions.SectionKey);
        var options = builder.Services.AddOptions<AmazonSnsOptions>().BindConfiguration(section);
        if (configure is not null)
        {
            options.Configure(configure);
        }

        options.ValidateOnStart();
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<AmazonSnsOptions>, AmazonSnsOptionsValidator>());
        builder.Services.TryAddSingleton<IAmazonSimpleNotificationService>(
            services => SnsClientOf(services.GetRequiredService<IOptions<AmazonSnsOptions>>().Value.Region, section));
        return builder.UseBrokerClient<AmazonSnsBrokerClient>();
    }

    private static AmazonSimpleNotificationServiceClient SnsClientOf(string? region, string section) =>
        region is null
            ? SnsClientOfTheRegionOfTheAwsSdk(section)
            : new AmazonSimpleNotificationServiceClient(RegionEndpoint.GetBySystemName(region));

    private static AmazonSimpleNotificationServiceClient SnsClientOfTheRegionOfTheAwsSdk(string section)
    {
        try
        {
            return new AmazonSimpleNotificationServiceClient();
        }
        catch (AmazonClientException)
        {
            throw new OptionsValidationException(
                nameof(AmazonSnsOptions), typeof(AmazonSnsOptions),
                [$"{nameof(AmazonSnsOptions.Region)}: the AWS SDK configuration has no region. Set {section}:{nameof(AmazonSnsOptions.Region)}, or a region of the AWS SDK, for example the environment variable AWS_REGION."]);
        }
    }
}
