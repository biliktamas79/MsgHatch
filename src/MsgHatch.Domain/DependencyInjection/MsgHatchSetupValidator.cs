using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MsgHatch.Domain.Outbox;

namespace MsgHatch.Domain.DependencyInjection;

/// <summary>
/// The startup validation of the setup for the role of an event consumer (section 4.7): the outbox stores, one
/// selection for each interface (D62), a router for each message type (D95), unique content types, and unique
/// message type names.
/// </summary>
internal sealed class MsgHatchSetupValidator(IServiceProvider services, MsgHatchSetup setup) : IValidateOptions<MsgHatchSetupCheck>
{
    public ValidateOptionsResult Validate(string? name, MsgHatchSetupCheck options)
    {
        string[] failures =
        [
            .. MissingOutboxStores(),
            .. DuplicateSelections(),
            .. DuplicateContentTypes(),
            .. DuplicateMessageTypes(),
            .. MessageTypesWithNoRouter(),
        ];
        return failures.Length == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

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
