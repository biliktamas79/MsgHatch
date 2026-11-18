using Microsoft.Extensions.Options;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Pipeline;
using MsgHatch.Domain.Routing;
using MsgHatch.Subscriptions.DependencyInjection;

namespace MsgHatch.Subscriptions.Verification;

/// <summary>
/// The routing verifier on the subscription store and the recipient directory of its key (D85, D116, D126). For each
/// variation, it runs the routing procedure of the subscription router with the subscriptions of one store query and a
/// test profile, and composes the routes with <see cref="INotificationService.PreviewAsync{TEvent}"/>, so it stores
/// nothing and sends nothing (D100, section 4.18). It runs <see cref="RoutingVerificationRules"/> first (D248).
/// </summary>
internal sealed class RoutingVerifier(
    ISubscriptionStore store,
    IRecipientDirectory directory,
    IMessageRegistry registry,
    IEnumerable<ISubscriptionMessage> messages,
    INotificationService notifications,
    TimeProvider time,
    IOptions<MsgHatchOptions> options,
    TenantKey key,
    IServiceProvider services)
    : IRoutingVerifier
{
    public async ValueTask<ErrorOr<RoutingVerificationReport>> VerifyAsync(RoutingVerificationRequest request, CancellationToken ct)
    {
        if (RoutingVerificationRules.Check(request) is { } invalid)
        {
            return invalid;
        }

        var subscriptions = await store.QueryAsync(new SubscriptionQuery(request.Type), ct);
        if (subscriptions.IsError)
        {
            return subscriptions.Error;
        }

        var profiles = await directory.GetUsersAsync(request.TestUsers, ct);
        if (profiles.IsError)
        {
            return profiles.Error;
        }

        var run = new VerificationRun(request, subscriptions.Value, profiles.Value);
        foreach (var (type, message) in ScopedMessageTypes(request.Type))
        {
            if (!message.HasTestCases)
            {
                run.SkipMessageTypeWithNoTestEvent(type);
                continue;
            }

            run.StartMessageType(type);
            var error = await message.VerifyAsync(this, run, type, ct);
            if (error is not null)
            {
                return error;
            }
        }

        return run.Report();
    }

    /// <summary>Checks each test case, test user, and language of one message type, and gives back only a cancellation.</summary>
    public async ValueTask<Error?> VerifyTestCasesAsync<TEvent>(
        SubscriptionMessage<TEvent> message, MessageType type, VerificationRun run, CancellationToken ct)
    {
        foreach (var (testCase, create) in message.TestCases)
        {
            foreach (var user in run.Request.TestUsers)
            {
                foreach (var language in run.Request.Languages)
                {
                    var variation = new Variation(type, testCase, language);
                    var error = run.ProfileOf(user) is { } profile
                        ? await CheckAsync(message, variation, create(user), profile with { UiLanguage = language }, run, ct)
                        : run.Fail(variation, new Error(Errors.NotFound, "The directory does not know the test user."));
                    if (error is not null)
                    {
                        return error;
                    }
                }
            }
        }

        return null;
    }

    private async ValueTask<Error?> CheckAsync<TEvent>(
        SubscriptionMessage<TEvent> message, Variation variation, TEvent domainEvent, UserProfile testProfile, VerificationRun run,
        CancellationToken ct)
    {
        var router = new SubscriptionRouter<TEvent>(
            run.Store, new TestProfileDirectory(directory, testProfile), registry, options, message,
            SubscriptionLoggers.For<SubscriptionRouter<TEvent>>(services));
        var context = new MessageContext(Guid.CreateVersion7(time.GetUtcNow()), key.Tenant, MessageFlags.Test);
        var resolution = await router.ResolveAsync(domainEvent, variation.Type, context, ct);
        if (resolution.IsError)
        {
            return run.Fail(variation, resolution.Error);
        }

        var results = await notifications.PreviewAsync(domainEvent, context, resolution.Value.Deliveries, ct);
        if (results.IsError)
        {
            return run.Fail(variation, results.Error);
        }

        run.Add(variation, resolution.Value, results.Value);
        return null;
    }

    private IEnumerable<(MessageType Type, ISubscriptionMessage Message)> ScopedMessageTypes(MessageType? only)
    {
        foreach (var descriptor in registry.MessageTypes)
        {
            if ((only is null || descriptor.Type == only)
                && messages.FirstOrDefault(message => message.HasScope && message.EventType == descriptor.EventType) is { } scoped)
            {
                yield return (descriptor.Type, scoped);
            }
        }
    }
}
