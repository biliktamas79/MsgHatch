using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Inbox;
using MsgHatch.Domain.Outbox;
using MsgHatch.Domain.Routing;
using MsgHatch.Domain.Telemetry;
using MsgHatch.Domain.Time;

namespace MsgHatch.Domain.Pipeline;

/// <summary>
/// The steps 1 to 6 of the processing of one event (section 4.8): accept into the inbox, or route, check, compose,
/// build the outgoing messages, and store them. Each step resolves its tenant services with the tenant key of the
/// event (D126). After a store, it notifies the dispatch signal, and a notify error only logs a warning (D102).
/// </summary>
internal sealed class NotificationPipeline(
    IServiceProvider services, IMessageRegistry registry, IOptionsMonitor<RetryOptions> retryOptions, TimeProvider time,
    MsgHatchMetrics metrics, IDispatchSignal signal)
{
    private const int _oneProcessingInMemory = 1;

    public async ValueTask<ErrorOr<Guid>> AcceptAsync<TEvent>(TEvent domainEvent, MessageContext context, CancellationToken ct)
    {
        var found = FindRegistration<TEvent>();
        if (found.IsError)
        {
            return found.Error;
        }

        var inbox = services.GetKeyedService<IInboxStore>(TenantKey.For(context.Tenant));
        if (inbox is null)
        {
            return new Error(Errors.NotFound, "No setup method selects an IInboxStore, for example UseInMemoryStores.");
        }

        metrics.RecordReceived(found.Value.Type, context.Flags);
        var received = found.Value.Receive(
            domainEvent, context, time.GetUtcNowDateTime(), retryOptions.Get(OptionNames.Inbox).MaxTryCount, TraceParents.Current());
        var error = await inbox.AddAsync(received, ct);
        if (error is not null)
        {
            return error;
        }

        await NotifyAsync([DispatchTopic.Inbox], ct);
        return context.MessageId;
    }

    public async ValueTask<ErrorOr<NotificationResult>> ProcessAsync<TEvent>(
        TEvent domainEvent, MessageContext context, CancellationToken ct)
    {
        var found = FindRegistration<TEvent>();
        if (found.IsError)
        {
            return found.Error;
        }

        var registration = found.Value;
        var started = time.GetTimestamp();
        using var span = ProcessSpan.Start(registration.Type, context);
        metrics.RecordReceived(registration.Type, context.Flags);
        return await MeasureAsync(registration.Type, context.Flags, started, span, () =>
        {
            var nowUtc = time.GetUtcNowDateTime();
            var received = registration.Receive(domainEvent, context, nowUtc, _oneProcessingInMemory, span.CallerTraceParent);
            return ProcessInSpanAsync(registration, received, nowUtc, span.TraceParent, ct);
        }, ct);
    }

    /// <summary>
    /// Runs the steps 2 to 6 for a received message that the inbox processor claimed (section 4.8), in a
    /// <c>process</c> span of a new trace with a link to the trace context of the message (D132).
    /// </summary>
    public async ValueTask<ErrorOr<NotificationResult>> ProcessReceivedAsync<TEvent>(
        MessageRegistration<TEvent> registration, ReceivedMessage<TEvent> received, CancellationToken ct)
    {
        var started = time.GetTimestamp();
        using var span = ProcessSpan.StartForReceived(received);
        return await MeasureAsync(
            registration.Type, received.Flags, started, span,
            () => ProcessInSpanAsync(registration, received, time.GetUtcNowDateTime(), span.TraceParent, ct), ct);
    }

    private async ValueTask<ErrorOr<NotificationResult>> MeasureAsync(
        MessageType type, MessageFlags flags, long started, ProcessSpan span, Func<ValueTask<ErrorOr<NotificationResult>>> process,
        CancellationToken ct)
    {
        ErrorOr<NotificationResult> result = default;
        try
        {
            result = await process();
            return result;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            result = new Error(Errors.OperationCancelled);
            throw;
        }
        catch (Exception)
        {
            result = new Error(Errors.Exception);
            throw;
        }
        finally
        {
            span.Finish(result);
            metrics.RecordProcessed(type, flags, result.Error, time.GetElapsedTime(started));
        }
    }

    private async ValueTask<ErrorOr<NotificationResult>> ProcessInSpanAsync<TEvent>(
        MessageRegistration<TEvent> registration, ReceivedMessage<TEvent> received, DateTime nowUtc, string? outgoingTraceParent,
        CancellationToken ct)
    {
        var composed = await RouteAndComposeAsync(registration, received, nowUtc, givenRoutes: null, outgoingTraceParent, ct);
        if (composed.IsError)
        {
            return composed.Error;
        }

        RecordFailedRoutes(registration.Type, composed.Value.Routes, composed.Value.Outcomes);
        return await StoreAsync(composed.Value.Key, composed.Value.Outcomes, ct);
    }

    private void RecordFailedRoutes(MessageType type, IReadOnlyList<MessageRoute> routes, ErrorOr<OutgoingMessage>[] outcomes)
    {
        for (var position = 0; position < outcomes.Length; position++)
        {
            if (outcomes[position].IsError)
            {
                metrics.RecordRouteFailed(type, routes[position].Channel, outcomes[position].Error!);
            }
        }
    }

    private void RecordStored(IReadOnlyList<OutgoingMessage> messages)
    {
        foreach (var message in messages)
        {
            metrics.RecordStored(message.Type, message.Channel, message.Flags);
        }
    }

    public async ValueTask<ErrorOr<IReadOnlyList<RouteResult>>> PreviewAsync<TEvent>(
        TEvent domainEvent, MessageContext context, IReadOnlyList<MessageRoute>? routes, CancellationToken ct)
    {
        var found = FindRegistration<TEvent>();
        if (found.IsError)
        {
            return found.Error;
        }

        var nowUtc = time.GetUtcNowDateTime();
        var received = found.Value.Receive(domainEvent, AsTestMessage(context), nowUtc, _oneProcessingInMemory, traceParent: null);
        var composed = await RouteAndComposeAsync(found.Value, received, nowUtc, routes, outgoingTraceParent: null, ct);
        return composed.IsError
            ? composed.Error
            : ErrorOr<IReadOnlyList<RouteResult>>.ForValue(ToRouteResults(composed.Value.Routes, composed.Value.Outcomes));
    }

    private ErrorOr<MessageRegistration<TEvent>> FindRegistration<TEvent>() =>
        services.GetService<MessageRegistration<TEvent>>() is { } registration
            ? registration
            : new Error(Errors.NotFound, $"The event type {typeof(TEvent).Name} is not registered.");

    private async ValueTask<ErrorOr<ComposedEvent>> RouteAndComposeAsync<TEvent>(
        MessageRegistration<TEvent> registration, ReceivedMessage<TEvent> received, DateTime nowUtc,
        IReadOnlyList<MessageRoute>? givenRoutes, string? outgoingTraceParent, CancellationToken ct)
    {
        var key = TenantKey.For(received.Tenant);
        var routes = givenRoutes is null
            ? await RouteAsync(registration, received, key, ct)
            : ErrorOr<IReadOnlyList<MessageRoute>>.ForValue(givenRoutes);
        if (routes.IsError)
        {
            return routes.Error;
        }

        var outcomes = await ComposeAsync(
            registration, received, routes.Value, key, new OutgoingDefaults(nowUtc, outgoingTraceParent, retryOptions), ct);
        return outcomes.IsError ? outcomes.Error : new ComposedEvent(key, routes.Value, outcomes.Value);
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
            var channel = checkedRoutes[group.Positions.Span[0]].Channel;
            var messages = await channel.ComposeAsync(services, key, received, group.Routes, defaults, ct);
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

    private List<(int Position, MessageRoute Route, IChannelRegistration<TEvent> Channel)> Check<TEvent>(
        MessageRegistration<TEvent> registration, IReadOnlyList<MessageRoute> routes, ErrorOr<OutgoingMessage>[] outcomes)
    {
        var checkedRoutes = new List<(int Position, MessageRoute Route, IChannelRegistration<TEvent> Channel)>(routes.Count);
        var targetsOnChannels = new HashSet<(DeliveryTarget Target, ChannelId Channel)>();
        for (var position = 0; position < routes.Count; position++)
        {
            var channel = CheckRoute(registration, routes[position], targetsOnChannels);
            if (channel.IsError)
            {
                outcomes[position] = channel.Error;
            }
            else
            {
                checkedRoutes.Add((position, routes[position], channel.Value));
            }
        }

        return checkedRoutes;
    }

    private ErrorOr<IChannelRegistration<TEvent>> CheckRoute<TEvent>(
        MessageRegistration<TEvent> registration, MessageRoute route, HashSet<(DeliveryTarget, ChannelId)> targetsOnChannels)
    {
        var descriptor = registry.FindChannel(route.Channel);
        var channel = registration.FindChannel(route.Channel);
        if (descriptor is null || channel is null)
        {
            return new Error(Errors.NotFound, $"The message type {registration.Type.Name} has no registration on the channel {route.Channel.Name}.");
        }

        if (route.Settings?.GetType() != descriptor.SettingsType)
        {
            return new Error(Errors.ValidationFailed, $"A route on the channel {route.Channel.Name} has settings of another type than {descriptor.SettingsType.Name}.");
        }

        return targetsOnChannels.Add((route.Target, route.Channel))
            ? ErrorOr<IChannelRegistration<TEvent>>.ForValue(channel)
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

            RecordStored(messages);
            await NotifyAsync(messages.Select(static message => DispatchTopic.ForChannel(message.Channel)).Distinct(), ct);
        }

        return new NotificationResult(messages.Length, errors.Length, errors);
    }

    private async ValueTask NotifyAsync(IEnumerable<DispatchTopic> topics, CancellationToken ct)
    {
        foreach (var topic in topics)
        {
            var error = await signal.NotifyAsync(topic, ct);
            if (error is not null)
            {
                Log.NotifyFailed(Loggers.For<NotificationPipeline>(services), topic.Name, error.Type.Code, error.Type.Name, error.Message);
            }
        }
    }

    private readonly record struct ComposedEvent(TenantKey Key, IReadOnlyList<MessageRoute> Routes, ErrorOr<OutgoingMessage>[] Outcomes);
}
