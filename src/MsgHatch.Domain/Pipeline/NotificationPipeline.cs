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

    /// <summary>The outcome of a checked route until the composer of its channel writes the outgoing message or the error.</summary>
    private static readonly Error _waitsForItsComposer = new(Errors.Uninitialized, "The route passed the check, and waits for the composer of its channel.");

    public async ValueTask<ErrorOr<Guid>> AcceptAsync<TEvent>(TEvent domainEvent, MessageContext context, CancellationToken ct)
    {
        var found = FindRegistration<TEvent>();
        if (found.IsError)
        {
            return found.Error;
        }

        var inbox = services.GetKeyedService<IInboxStore>(TenantKeyBoxes.Of(TenantKey.For(context.Tenant)));
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

        await NotifyAsync(DispatchTopic.Inbox, ct);
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
        var nowUtc = time.GetUtcNowDateTime();
        var received = registration.Receive(domainEvent, context, nowUtc, _oneProcessingInMemory, span.CallerTraceParent);
        return await MeasureAsync(registration, received, nowUtc, started, span, ct);
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
        return await MeasureAsync(registration, received, time.GetUtcNowDateTime(), started, span, ct);
    }

    private async ValueTask<ErrorOr<NotificationResult>> MeasureAsync<TEvent>(
        MessageRegistration<TEvent> registration, ReceivedMessage<TEvent> received, DateTime nowUtc, long started, ProcessSpan span,
        CancellationToken ct)
    {
        ErrorOr<NotificationResult> result = default;
        try
        {
            result = await ProcessInSpanAsync(registration, received, nowUtc, span.TraceParent, ct);
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
            metrics.RecordProcessed(registration.Type, received.Flags, result.Error, time.GetElapsedTime(started));
        }
    }

    /// <summary>
    /// Runs the steps 2 to 6 with the registry settings (D118): a disabled message type stores nothing and calls no
    /// router, and a route on a disabled channel is skipped. A preview ignores the settings (D169).
    /// </summary>
    private async ValueTask<ErrorOr<NotificationResult>> ProcessInSpanAsync<TEvent>(
        MessageRegistration<TEvent> registration, ReceivedMessage<TEvent> received, DateTime nowUtc, string? outgoingTraceParent,
        CancellationToken ct)
    {
        await WaitForTheFirstRegistryReadAsync(ct);
        var descriptor = registry.FindMessageType(registration.Type);
        if (descriptor is { Enabled: false })
        {
            Loggers.For<NotificationPipeline>(services).MessageTypeDisabled(
                received.Delivery.Id, registration.Type.Name, received.Tenant?.Value, received.Flags);
            return new NotificationResult(0, 0, []);
        }

        var composed = await RouteAndComposeAsync(
            registration, received, nowUtc, givenRoutes: null, outgoingTraceParent, skipDisabledChannels: true, descriptor, ct);
        if (composed.IsError)
        {
            return composed.Error;
        }

        RecordFailedRoutes(registration.Type, composed.Value.Routes, composed.Value.Outcomes);
        return await StoreAsync(registration, composed.Value.Key, composed.Value.Outcomes, composed.Value.Skipped, ct);
    }

    /// <summary>Waits for the first read of the stored registry, so that its settings apply after a restart too (D168).</summary>
    private ValueTask WaitForTheFirstRegistryReadAsync(CancellationToken ct) =>
        registry is StoredRegistry { FirstRead.IsCompleted: false } stored ? new ValueTask(stored.FirstRead.WaitAsync(ct)) : ValueTask.CompletedTask;

    /// <summary>The routes that are not on a disabled channel. With no skipped route, it gives the same list and allocates nothing.</summary>
    private IReadOnlyList<MessageRoute> EnabledRoutes(MessageTypeDescriptor? type, ReceivedMessage received, IReadOnlyList<MessageRoute> routes)
    {
        List<MessageRoute>? enabled = null;
        for (var position = 0; position < routes.Count; position++)
        {
            var route = routes[position];
            if (IsOnADisabledChannel(type, route))
            {
                enabled ??= [.. routes.Take(position)];
                Loggers.For<NotificationPipeline>(services).RouteSkipped(
                    received.Delivery.Id, received.Type.Name, received.Tenant?.Value, received.Flags, route.Channel.Name);
            }
            else
            {
                enabled?.Add(route);
            }
        }

        return enabled ?? routes;
    }

    private bool IsOnADisabledChannel(MessageTypeDescriptor? type, MessageRoute route) =>
        registry.FindChannel(route.Channel) is { Enabled: false } || type?.DisabledChannels?.Contains(route.Channel) == true;

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

    private void RecordStored(OutgoingMessage[] messages)
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
        var composed = await RouteAndComposeAsync(
            found.Value, received, nowUtc, routes, outgoingTraceParent: null, skipDisabledChannels: false, descriptor: null, ct);
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
        IReadOnlyList<MessageRoute>? givenRoutes, string? outgoingTraceParent, bool skipDisabledChannels, MessageTypeDescriptor? descriptor,
        CancellationToken ct)
    {
        var key = TenantKey.For(received.Tenant);
        var routes = givenRoutes is null
            ? await RouteAsync(registration, received, key, ct)
            : ErrorOr<IReadOnlyList<MessageRoute>>.ForValue(givenRoutes);
        if (routes.IsError)
        {
            return routes.Error;
        }

        var selected = skipDisabledChannels ? EnabledRoutes(descriptor, received, routes.Value) : routes.Value;
        var outcomes = await ComposeAsync(
            registration, received, selected, key, new OutgoingDefaults(nowUtc, outgoingTraceParent, retryOptions), ct);
        return outcomes.IsError ? outcomes.Error : new ComposedEvent(key, selected, outcomes.Value, routes.Value.Count - selected.Count);
    }

    private static MessageContext AsTestMessage(MessageContext context) => context with { Flags = context.Flags | MessageFlags.Test };

    private static RouteResult[] ToRouteResults(IReadOnlyList<MessageRoute> routes, ErrorOr<OutgoingMessage>[] outcomes) =>
        [.. routes.Select((route, position) => new RouteResult(
            route, outcomes[position].IsSuccess ? outcomes[position].Value : null, outcomes[position].Error))];

    private ValueTask<ErrorOr<IReadOnlyList<MessageRoute>>> RouteAsync<TEvent>(
        MessageRegistration<TEvent> registration, ReceivedMessage<TEvent> received, TenantKey key, CancellationToken ct)
    {
        var router = services.GetKeyedService<IMessageRouter<TEvent>>(TenantKeyBoxes.Of(key));
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
        var channels = Check(registration, routes, outcomes);
        if (channels.HasOneChannelFor(routes.Count) && routes is MessageRoute[] routesOfTheRouter)
        {
            var error = await channels[0].Channel.ComposeAsync(
                services, key, received, routesOfTheRouter, positions: ReadOnlyMemory<int>.Empty, outcomes, defaults, ct);
            return error is null ? outcomes : error;
        }

        if (channels.Count == 0)
        {
            return outcomes;
        }

        var (grouped, positions) = GroupByChannel(routes, outcomes, channels);
        var start = 0;
        for (var index = 0; index < channels.Count; index++)
        {
            var (channel, count) = channels[index];
            var error = await channel.ComposeAsync(
                services, key, received, grouped.AsMemory(start, count), positions.AsMemory(start, count), outcomes, defaults, ct);
            if (error is not null)
            {
                return error;
            }

            start += count;
        }

        return outcomes;
    }

    /// <summary>
    /// Checks each route. A failed route gets its error in <paramref name="outcomes"/>, and a checked route gets
    /// <see cref="_waitsForItsComposer"/>. One route needs no set of the targets.
    /// </summary>
    private EventChannels<TEvent> Check<TEvent>(
        MessageRegistration<TEvent> registration, IReadOnlyList<MessageRoute> routes, ErrorOr<OutgoingMessage>[] outcomes)
    {
        var channels = new EventChannels<TEvent>();
        var targetsOnChannels = routes.Count > 1 ? new HashSet<(DeliveryTarget, ChannelId)>(routes.Count) : null;
        for (var position = 0; position < routes.Count; position++)
        {
            var channel = CheckRoute(registration, routes[position], targetsOnChannels);
            if (channel.IsError)
            {
                outcomes[position] = channel.Error;
            }
            else
            {
                outcomes[position] = _waitsForItsComposer;
                channels.Add(channel.Value);
            }
        }

        return channels;
    }

    /// <summary>
    /// Gives one array with the checked routes of all channels, in groups by channel and in the order of the router, and
    /// the position of each of these routes in the outcomes.
    /// </summary>
    private static (MessageRoute[] Routes, int[] Positions) GroupByChannel<TEvent>(
        IReadOnlyList<MessageRoute> routes, ErrorOr<OutgoingMessage>[] outcomes, EventChannels<TEvent> channels)
    {
        var grouped = new MessageRoute[channels.RouteCount];
        var positions = new int[grouped.Length];
        var next = 0;
        for (var index = 0; index < channels.Count; index++)
        {
            var channel = channels[index].Channel.Channel;
            for (var position = 0; position < routes.Count; position++)
            {
                if (routes[position].Channel == channel && PassedTheCheck(outcomes[position]))
                {
                    grouped[next] = routes[position];
                    positions[next++] = position;
                }
            }
        }

        return (grouped, positions);
    }

    private static bool PassedTheCheck(ErrorOr<OutgoingMessage> outcome) => ReferenceEquals(outcome.Error, _waitsForItsComposer);

    private ErrorOr<IChannelRegistration<TEvent>> CheckRoute<TEvent>(
        MessageRegistration<TEvent> registration, MessageRoute route, HashSet<(DeliveryTarget, ChannelId)>? targetsOnChannels)
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

        return targetsOnChannels is null || targetsOnChannels.Add((route.Target, route.Channel))
            ? ErrorOr<IChannelRegistration<TEvent>>.ForValue(channel)
            : new Error(Errors.Conflict, $"A second route for the same target on the channel {route.Channel.Name}.");
    }

    private async ValueTask<ErrorOr<NotificationResult>> StoreAsync<TEvent>(
        MessageRegistration<TEvent> registration, TenantKey key, ErrorOr<OutgoingMessage>[] outcomes, int skipped, CancellationToken ct)
    {
        var (messages, errors) = SplitByOutcome(outcomes);
        if (messages.Length > 0)
        {
            var error = await services.GetRequiredKeyedService<IOutboxStore>(TenantKeyBoxes.Of(key)).AddAsync(messages, ct);
            if (error is not null)
            {
                return error;
            }

            RecordStored(messages);
            await NotifyEachChannelOneTimeAsync(registration, messages, ct);
        }

        return new NotificationResult(messages.Length, errors.Length, errors, skipped);
    }

    /// <summary>Gives the outgoing messages and the errors of the outcomes, in the order of the routes, each in an array of its exact size.</summary>
    private static (OutgoingMessage[] Messages, Error[] Errors) SplitByOutcome(ErrorOr<OutgoingMessage>[] outcomes)
    {
        var failed = 0;
        foreach (var outcome in outcomes)
        {
            failed += outcome.IsError ? 1 : 0;
        }

        OutgoingMessage[] messages = failed == outcomes.Length ? [] : new OutgoingMessage[outcomes.Length - failed];
        Error[] errors = failed == 0 ? [] : new Error[failed];
        var (nextMessage, nextError) = (0, 0);
        foreach (var outcome in outcomes)
        {
            if (outcome.IsError)
            {
                errors[nextError++] = outcome.Error!;
            }
            else
            {
                messages[nextMessage++] = outcome.Value;
            }
        }

        return (messages, errors);
    }

    /// <summary>Notifies the topic of each channel with a stored message one time, in the order of the first stored message of the channel.</summary>
    private async ValueTask NotifyEachChannelOneTimeAsync<TEvent>(
        MessageRegistration<TEvent> registration, OutgoingMessage[] messages, CancellationToken ct)
    {
        var first = messages[0].Channel;
        List<ChannelId>? others = null;
        await NotifyAsync(registration.FindChannel(first)!.Topic, ct);
        foreach (var message in messages)
        {
            if (message.Channel == first || others?.Contains(message.Channel) == true)
            {
                continue;
            }

            (others ??= []).Add(message.Channel);
            await NotifyAsync(registration.FindChannel(message.Channel)!.Topic, ct);
        }
    }

    private async ValueTask NotifyAsync(DispatchTopic topic, CancellationToken ct)
    {
        var error = await signal.NotifyAsync(topic, ct);
        if (error is not null)
        {
            Loggers.For<NotificationPipeline>(services).NotifyFailed(topic.Name, error.Type.Code, error.Type.Name, error.Message);
        }
    }

    private readonly record struct ComposedEvent(
        TenantKey Key, IReadOnlyList<MessageRoute> Routes, ErrorOr<OutgoingMessage>[] Outcomes, int Skipped);
}
