using Microsoft.Extensions.Options;

namespace MsgHatch.Subscriptions.DependencyInjection;

/// <summary>
/// The startup validation of the subscriptions setup (section 4.7): one selection of the subscription store and one of
/// the recipient directory (D62).
/// </summary>
internal sealed class SubscriptionSetupValidator(SubscriptionSetup setup) : IValidateOptions<SubscriptionSetupCheck>
{
    public ValidateOptionsResult Validate(string? name, SubscriptionSetupCheck options)
    {
        string[] failures =
        [
            .. DuplicateSelections(setup.StoreSelections, nameof(ISubscriptionStore)),
            .. DuplicateSelections(setup.DirectorySelections, nameof(IRecipientDirectory)),
        ];
        return failures.Length == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static IEnumerable<string> DuplicateSelections(List<string> selections, string service) =>
        selections.Count > 1 ? [$"{string.Join(" and ", selections)} select an implementation of {service}."] : [];
}
