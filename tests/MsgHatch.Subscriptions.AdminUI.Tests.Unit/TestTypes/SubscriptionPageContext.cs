using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Persistence;
using MsgHatch.Subscriptions.AdminUI.DependencyInjection;
using MsgHatch.Subscriptions.Management;
using MsgHatch.Subscriptions.Verification;
using NSubstitute;
using static MsgHatch.Subscriptions.AdminUI.Tests.Unit.TestTypes.SubscriptionTestData;

namespace MsgHatch.Subscriptions.AdminUI.Tests.Unit.TestTypes;

public abstract class SubscriptionPageContext : BunitContext
{
    protected SubscriptionPageContext()
    {
        Registry.Entries.Returns([FaxSettingsEntry]);
        Queries.GetChannelsAsync(Arg.Any<CancellationToken>()).Returns(Ok(Channels));
        Queries.GetMessageTypesAsync(new MessageTypeQuery(), Arg.Any<CancellationToken>())
            .Returns(Ok<IReadOnlyList<MessageTypeState>>([new MessageTypeState(Requests, [Fax], []), new MessageTypeState(Notices, [], [])]));
        Services.AddKeyedSingleton(TenantKey.None, Queries);
        Services.AddKeyedSingleton(TenantKey.None, Commands);
        Services.AddKeyedSingleton(TenantKey.None, Verifier);
        Services.AddSingleton(Registry);
        Services.AddMsgHatchSubscriptionsAdminUI();
    }

    protected ISubscriptionQueries Queries { get; } = Substitute.For<ISubscriptionQueries>();

    protected ISubscriptionCommands Commands { get; } = Substitute.For<ISubscriptionCommands>();

    protected IRoutingVerifier Verifier { get; } = Substitute.For<IRoutingVerifier>();

    protected IContentTypeRegistry Registry { get; } = Substitute.For<IContentTypeRegistry>();

    protected NavigationManager Navigation => Services.GetRequiredService<NavigationManager>();
}
