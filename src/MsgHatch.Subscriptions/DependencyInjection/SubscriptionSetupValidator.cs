using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MsgHatch.Domain.DependencyInjection;

namespace MsgHatch.Subscriptions.DependencyInjection;

/// <summary>
/// The startup validation of the subscriptions setup (section 4.7): one selection of the subscription store and one of
/// the recipient directory (D62), and with <c>UseSubscriptions</c> and the role <see cref="MsgHatchRoles.Consumer"/>, a
/// subscription store and a recipient directory.
/// </summary>
internal sealed class SubscriptionSetupValidator(IServiceProvider services, SubscriptionSetup setup) : IValidateOptions<SubscriptionSetupCheck>
{
    public ValidateOptionsResult Validate(string? name, SubscriptionSetupCheck options)
    {
        var routes = setup.UsesSubscriptions
            && services.GetRequiredService<IOptions<MsgHatchOptions>>().Value.Roles.HasFlag(MsgHatchRoles.Consumer);
        string[] failures =
        [
            .. DuplicateSelections(setup.StoreSelections, nameof(ISubscriptionStore)),
            .. DuplicateSelections(setup.DirectorySelections, nameof(IRecipientDirectory)),
            .. routes ? MissingStoreOrDirectory() : [],
        ];
        return failures.Length == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static IEnumerable<string> DuplicateSelections(List<string> selections, string service) =>
        selections.Count > 1 ? [$"{string.Join(" and ", selections)} select an implementation of {service}."] : [];

    private IEnumerable<string> MissingStoreOrDirectory()
    {
        if (setup.StoreSelections.Count == 0)
        {
            yield return "UseSubscriptions needs a subscription store, for example UseInMemorySubscriptionStore.";
        }

        if (setup.DirectorySelections.Count == 0)
        {
            yield return "UseSubscriptions needs a recipient directory. Select one with UseRecipientDirectory.";
        }
    }
}
