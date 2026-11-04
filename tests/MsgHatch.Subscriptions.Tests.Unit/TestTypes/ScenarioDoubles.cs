using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using NSubstitute;
using NSubstitute.Core;

namespace MsgHatch.Subscriptions.Tests.Unit.TestTypes;

/// <summary>
/// Substitutes for the subscription store and the recipient directory that answer from given data, and the calls that
/// they received in the form <c>FindForDeliveryAsync(A; c:100; u:1,2)</c>.
/// </summary>
public static class ScenarioDoubles
{
    public static ISubscriptionStore AStore(IReadOnlyList<Subscription> subscriptions)
    {
        var store = Substitute.For<ISubscriptionStore>();
        store.FindForDeliveryAsync(default, default!, default!, default).ReturnsForAnyArgs(call =>
            ValueTask.FromResult(ErrorOr<IReadOnlyList<Subscription>>.ForValue(Scenario.SubscriptionsOf(call.ArgAt<MessageType>(0).Name, subscriptions))));
        return store;
    }

    public static IRecipientDirectory ADirectory(IReadOnlyList<UserProfile> users, IReadOnlyList<CompanyProfile> companies)
    {
        var directory = Substitute.For<IRecipientDirectory>();
        directory.GetUsersAsync(default!, default).ReturnsForAnyArgs(call => ValueTask.FromResult(
            ErrorOr<IReadOnlyList<UserProfile>>.ForValue([.. users.Where(user => call.ArgAt<IReadOnlyList<UserId>>(0).Contains(user.Id))])));
        directory.GetCompanyUsersAsync(default, default).ReturnsForAnyArgs(call => ValueTask.FromResult(
            ErrorOr<IReadOnlyList<UserProfile>>.ForValue([.. users.Where(user => user.Company == call.ArgAt<CompanyId>(0))])));
        directory.GetCompanyAsync(default, default).ReturnsForAnyArgs(call => ValueTask.FromResult(
            ErrorOr<CompanyProfile?>.ForValue(companies.FirstOrDefault(company => company.Id == call.ArgAt<CompanyId>(0)))));
        return directory;
    }

    public static IRecipientDirectory AScenarioDirectory() => ADirectory(Scenario.Users, Scenario.Companies);

    public static ISubscriptionStore AScenarioStore() => AStore(Scenario.Subscriptions);

    public static string[] Calls(object substitute) =>
        [.. substitute.ReceivedCalls().Select(static call => $"{call.GetMethodInfo().Name}({string.Join("; ", Arguments(call))})")];

    private static IEnumerable<string> Arguments(ICall call) =>
        call.GetArguments().Where(static argument => argument is not CancellationToken).Select(static argument => argument switch
        {
            MessageType type => type.Name,
            CompanyId company => $"c:{company.Value}",
            IReadOnlyList<CompanyId> companies => $"c:{string.Join(',', companies.Select(static company => company.Value))}",
            IReadOnlyList<UserId> users => $"u:{string.Join(',', users.Select(static user => user.Value))}",
            _ => argument?.ToString() ?? "null",
        });
}
