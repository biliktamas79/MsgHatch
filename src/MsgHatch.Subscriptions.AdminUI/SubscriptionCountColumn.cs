using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using MsgHatch.AdminUI;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Management;
using MsgHatch.Subscriptions.Management;

namespace MsgHatch.Subscriptions.AdminUI;

/// <summary>
/// The column "Subscriptions" of the message types page (D276): the number of the stored subscriptions of each message type
/// for each <see cref="SubscriptionLevel"/>, also 0, so the badges of the rows align. Each count links to the subscription
/// list with the filters of its message type and its level. It reads the registered message types, so each of them gets
/// its counts, and the stored subscriptions with one call.
/// </summary>
/// <param name="services">The services of the scope of the page.</param>
internal sealed class SubscriptionCountColumn(IServiceProvider services) : IMessageTypeColumn
{
    private static readonly SubscriptionLevel[] _levels = Enum.GetValues<SubscriptionLevel>();

    public string Header => "Subscriptions";

    public async ValueTask<ErrorOr<IReadOnlyDictionary<MessageType, IReadOnlyList<MessageTypeCellItem>>>> GetCellsAsync(CancellationToken ct)
    {
        var types = await services.GetRequiredKeyedService<IRegistryQueries>(TenantKey.None).GetMessageTypesAsync(ct);
        if (types.IsError)
        {
            return types.Error;
        }

        var subscriptions = await services.GetRequiredKeyedService<ISubscriptionQueries>(TenantKey.None).GetSubscriptionsAsync(new SubscriptionQuery(), ct);
        if (subscriptions.IsError)
        {
            return subscriptions.Error;
        }

        var counts = subscriptions.Value
            .GroupBy(static subscription => (subscription.Type, subscription.Owner.Level))
            .ToDictionary(static group => group.Key, static group => group.Count());
        return types.Value.DistinctBy(static type => type.Type).ToDictionary(
            static type => type.Type,
            type => (IReadOnlyList<MessageTypeCellItem>)[.. _levels.Select(level => new MessageTypeCellItem(
                string.Create(CultureInfo.InvariantCulture, $"{level} {counts.GetValueOrDefault((type.Type, level))}"),
                SubscriptionAdminRoutes.ListOf(type.Type, level).TrimStart('/')))]);
    }
}
