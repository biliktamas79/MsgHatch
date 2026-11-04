using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Pipeline;
using MsgHatch.Domain.Routing;
using MsgHatch.Email.DependencyInjection;
using MsgHatch.Subscriptions.DependencyInjection;
using NSubstitute;

namespace MsgHatch.Subscriptions.Tests.Unit.TestTypes;

/// <summary>
/// The host of the subscription manager tests: the message types A to E of the scenario, the in-memory subscription store,
/// and a directory substitute. The message type F has the email channel and its own router, and no scope function. With
/// the test channel <c>Pager</c>, also the message type P on that channel.
/// </summary>
public static class ManagerHost
{
    public static readonly ChannelId Pager = new("Pager");

    public static ServiceProvider Build(
        TimeProvider time,
        IRecipientDirectory? directory = null,
        Func<TenantKey, ISubscriptionStore>? store = null,
        bool withPager = false,
        bool enableLogging = true)
    {
        var services = TestHost.CreateServices(time);
        var builder = services.AddMsgHatch(o => { o.Roles = MsgHatchRoles.Consumer; o.EnableLogging = enableLogging; })
            .UseSubscriptions()
            .UseInMemoryStores()
            .UseRecipientDirectory((_, _) => directory ?? ScenarioDoubles.AScenarioDirectory())
            .UseEmbeddedTemplates(typeof(ManagerHost).Assembly, SubscriptionHost.TemplatePrefix)
            .AddScenarioMessages()
            .AddMessage("F", 1, SubscriptionTestJson.Default.Notice, static m => m
                .WithRouter(static (_, _) => Substitute.For<IMessageRouter<Notice>>())
                .WithPayload<NoticeEmail, NoticeEmailLoader<Notice>>(static p => p.AsEmail("FEmail")));
        builder = store is null ? builder.UseInMemorySubscriptionStore() : builder.UseSubscriptionStore((_, key) => store(key));
        if (withPager)
        {
            AddPager(builder);
        }

        return services.Build();
    }

    public static async Task SeedScenarioAsync(this IServiceProvider provider)
    {
        var store = provider.GetRequiredService<ISubscriptionStore>();
        foreach (var subscription in Scenario.Subscriptions)
        {
            Assert.Null(await store.AddAsync(subscription, TestContext.Current.CancellationToken));
        }
    }

    private static void AddPager(MsgHatchBuilder builder)
    {
        var json = SubscriptionTestJson.Default;
        var validator = Substitute.For<IChannelSettingsValidator>();
        validator.Channel.Returns(Pager);
        validator.Validate(default!, default).ReturnsForAnyArgs(static call =>
            call.ArgAt<ChannelSettings>(0) is PagerSettings { Priority: < 0 } ? new Error(Errors.ValidationFailed) : null);
        builder
            .AddChannelType(new ChannelDescriptor<PagerSettings>(Pager, "Pager", ChannelFeatures.None), 1, json.PagerSettings, validator)
            .AddMessage("P", 1, json.PagerEvent, m => m
                .WithScope(static e => NotificationScope.ForUsers(e.Users))
                .AsChannel(Pager, "PagerNotice", 1, json.PagerEnvelope, static (_, _) => Substitute.For<IEnvelopeComposer<PagerEvent, PagerEnvelope>>()));
    }
}
