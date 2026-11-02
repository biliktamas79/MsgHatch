using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Outbox;

namespace MsgHatch.Domain.DependencyInjection;

/// <summary>
/// The startup validation of the setup (section 4.7): the outbox stores, one selection for each interface (D62), one
/// transport for each channel (D129), unique content types, unique message type names, and the rules of the channel
/// registrations and the payload loaders of each message type (D135). With the role
/// <see cref="MsgHatchRoles.Consumer"/>, a router for each message type (D95), and with the role
/// <see cref="MsgHatchRoles.Publisher"/>, at least one transport (D105).
/// </summary>
internal sealed class MsgHatchSetupValidator(IServiceProvider services, MsgHatchSetup setup) : IValidateOptions<MsgHatchSetupCheck>
{
    public ValidateOptionsResult Validate(string? name, MsgHatchSetupCheck options)
    {
        var roles = services.GetRequiredService<IOptions<MsgHatchOptions>>().Value.Roles;
        string[] failures =
        [
            .. MissingOutboxStores(),
            .. DuplicateSelections(),
            .. DuplicateTransports(),
            .. DuplicateContentTypes(),
            .. DuplicateMessageTypes(),
            .. setup.Messages.SelectMany(static message => message.RegistrationFailures()),
            .. roles.HasFlag(MsgHatchRoles.Consumer) ? MessageTypesWithNoRouter() : [],
            .. roles.HasFlag(MsgHatchRoles.Publisher) ? MissingTransport() : [],
        ];
        return failures.Length == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private IEnumerable<string> MissingTransport() =>
        services.GetServices<ITransport>().Any()
            ? []
            : ["The host has the role Publisher, but no setup method registers a transport, for example UseAmazonSes."];

    private IEnumerable<string> MissingOutboxStores() =>
        new[] { typeof(IOutboxStore), typeof(IOutboxDispatchStore) }
            .Where(store => !setup.Selections.Exists(selection => selection.Service == store))
            .Select(static store => $"No setup method selects an {store.Name}, for example UseInMemoryStores.");

    private IEnumerable<string> DuplicateSelections() =>
        setup.Selections
            .GroupBy(static selection => selection.Service)
            .Where(static group => group.Count() > 1)
            .Select(static group =>
                $"{string.Join(" and ", group.Select(static selection => selection.SetupMethod))} select an implementation of {group.Key.Name}.");

    private IEnumerable<string> DuplicateTransports() =>
        services.GetServices<ITransport>()
            .GroupBy(static transport => transport.Channel)
            .Where(static group => group.Count() > 1)
            .Select(static group => $"The channel {group.Key.Name} has {group.Count()} transports.");

    private IEnumerable<string> DuplicateContentTypes() =>
        setup.ContentTypes
            .GroupBy(static entry => entry.Discriminator, StringComparer.Ordinal)
            .Where(static group => group.Count() > 1)
            .Select(static group => $"The content type {group.Key} is registered {group.Count()} times.");

    private IEnumerable<string> DuplicateMessageTypes() =>
        setup.Messages
            .GroupBy(static message => message.Descriptor.Type)
            .Where(static group => group.Count() > 1)
            .Select(static group => $"The message type {group.Key.Name} is registered {group.Count()} times.");

    private IEnumerable<string> MessageTypesWithNoRouter()
    {
        using var scope = services.CreateScope();
        return [.. setup.Messages.Select(message => message.RouterFailure(scope.ServiceProvider)).OfType<string>()];
    }
}
