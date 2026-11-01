using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Outbox;
using MsgHatch.Domain.Routing;
using MsgHatch.Domain.Time;

namespace MsgHatch.Domain.Pipeline;

/// <summary>
/// The steps 2 to 6 of the processing of one event (section 4.8): route, check, compose, build the outgoing messages,
/// and store them. Each step resolves its tenant services with the tenant key of the event (D126).
/// </summary>
internal sealed class NotificationPipeline(
    IServiceProvider services, IMessageRegistry registry, IOptionsMonitor<RetryOptions> retryOptions, TimeProvider time)
{
    public async ValueTask<ErrorOr<NotificationResult>> ProcessAsync<TEvent>(
        TEvent domainEvent, MessageContext context, CancellationToken ct)
    {
        var registration = services.GetService<MessageRegistration<TEvent>>();
        if (registration is null)
        {
            return new Error(Errors.NotFound, $"The event type {typeof(TEvent).Name} is not registered.");
        }

        var key = TenantKey.For(context.Tenant);
        var nowUtc = time.GetUtcNowDateTime();
        var received = registration.ReceiveInMemory(domainEvent, context, nowUtc, traceParent: null);
        var routes = await RouteAsync(registration, received, key, ct);
        if (routes.IsError)
        {
            return routes.Error;
        }

        var outcomes = await ComposeAsync(registration, received, routes.Value, key, new OutgoingDefaults(nowUtc, null, retryOptions), ct);
        return outcomes.IsError ? outcomes.Error : await StoreAsync(key, outcomes.Value, ct);
    }

    public async ValueTask<ErrorOr<IReadOnlyList<RouteResult>>> PreviewAsync<TEvent>(
        TEvent domainEvent, MessageContext context, IReadOnlyList<MessageRoute>? routes, CancellationToken ct)
    {
        var registration = services.GetService<MessageRegistration<TEvent>>();
        if (registration is null)
        {
            return new Error(Errors.NotFound, $"The event type {typeof(TEvent).Name} is not registered.");
        }

        var key = TenantKey.For(context.Tenant);
        var nowUtc = time.GetUtcNowDateTime();
        var received = registration.ReceiveInMemory(domainEvent, AsTestMessage(context), nowUtc, traceParent: null);
        var previewRoutes = routes is null ? await RouteAsync(registration, received, key, ct) : ErrorOr<IReadOnlyList<MessageRoute>>.ForValue(routes);
        if (previewRoutes.IsError)
        {
            return previewRoutes.Error;
        }

        var outcomes = await ComposeAsync(registration, received, previewRoutes.Value, key, new OutgoingDefaults(nowUtc, null, retryOptions), ct);
        return outcomes.IsError ? outcomes.Error : ErrorOr<IReadOnlyList<RouteResult>>.ForValue(ToRouteResults(previewRoutes.Value, outcomes.Value));
    }

    private static MessageContext AsTestMessage(MessageContext context) => context with { Flags = context.Flags | MessageFlags.Test };

    private static RouteResult[] ToRouteResults(IReadOnlyList<MessageRoute> routes, ErrorOr<OutgoingMessage>[] outcomes) =>
        [.. routes.Select((route, position) => new RouteResult(
            route, outcomes[position].IsSuccess ? outcomes[position].Value : null, outcomes[position].Error))];

    private ValueTask<ErrorOr<IReadOnlyList<MessageRoute>>> RouteAsync<TEvent>(
        MessageRegistration<TEvent> registration, ReceivedMessage<TEvent> received, TenantKey key, CancellationToken ct)
    {
        var router = services.GetKeyedService<IMessageRouter<TEvent>>(key);
        return router is null
            ? ValueTask.FromResult<ErrorOr<IReadOnlyList<MessageRoute>>>(
                new Error(Errors.NotFound, $"The message type {registration.Type.Name} has no router."))
            : router.RouteAsync(received, ct);
    }

    private async ValueTask<ErrorOr<ErrorOr<OutgoingMessage>[]>> ComposeAsync<TEvent>(
        MessageRegistration<TEvent> registration, ReceivedMessage<TEvent> received, IReadOnlyList<MessageRoute> routes,
        TenantKey key, OutgoingDefaults defaults, CancellationToken ct)
    {
        var outcomes = new ErrorOr<OutgoingMessage>[routes.Count];
        var checkedRoutes = Check(registration, routes, outcomes);
        foreach (var group in RouteGrouping.ByChannel(checkedRoutes.Select(static route => route.Route).ToArray()))
        {
            var messages = await registration.FindChannel(group.Key)!.ComposeAsync(services, key, received, group.Routes, defaults, ct);
            if (messages.IsError)
            {
                return messages.Error;
            }

            for (var index = 0; index < group.Positions.Length; index++)
            {
                outcomes[checkedRoutes[group.Positions.Span[index]].Position] = messages.Value[index];
            }
        }

        return outcomes;
    }

    private List<(int Position, MessageRoute Route)> Check<TEvent>(
        MessageRegistration<TEvent> registration, IReadOnlyList<MessageRoute> routes, ErrorOr<OutgoingMessage>[] outcomes)
    {
        var checkedRoutes = new List<(int Position, MessageRoute Route)>(routes.Count);
        var targetsOnChannels = new HashSet<(DeliveryTarget Target, ChannelId Channel)>();
        for (var position = 0; position < routes.Count; position++)
        {
            var error = CheckRoute(registration, routes[position], targetsOnChannels);
            if (error is null)
            {
                checkedRoutes.Add((position, routes[position]));
            }
            else
            {
                outcomes[position] = error;
            }
        }

        return checkedRoutes;
    }

    private Error? CheckRoute<TEvent>(
        MessageRegistration<TEvent> registration, MessageRoute route, HashSet<(DeliveryTarget, ChannelId)> targetsOnChannels)
    {
        var channel = registry.FindChannel(route.Channel);
        if (channel is null || registration.FindChannel(route.Channel) is null)
        {
            return new Error(Errors.NotFound, $"The message type {registration.Type.Name} has no registration on the channel {route.Channel.Name}.");
        }

        if (route.Settings?.GetType() != channel.SettingsType)
        {
            return new Error(Errors.ValidationFailed, $"A route on the channel {route.Channel.Name} has settings of another type than {channel.SettingsType.Name}.");
        }

        return targetsOnChannels.Add((route.Target, route.Channel))
            ? null
            : new Error(Errors.Conflict, $"A second route for the same target on the channel {route.Channel.Name}.");
    }

    private async ValueTask<ErrorOr<NotificationResult>> StoreAsync(
        TenantKey key, ErrorOr<OutgoingMessage>[] outcomes, CancellationToken ct)
    {
        OutgoingMessage[] messages = [.. outcomes.Where(static outcome => outcome.IsSuccess).Select(static outcome => outcome.Value)];
        Error[] errors = [.. outcomes.Where(static outcome => outcome.IsError).Select(static outcome => outcome.Error!)];
        if (messages.Length > 0)
        {
            var error = await services.GetRequiredKeyedService<IOutboxStore>(key).AddAsync(messages, ct);
            if (error is not null)
            {
                return error;
            }
        }

        return new NotificationResult(messages.Length, errors.Length, errors);
    }
}
