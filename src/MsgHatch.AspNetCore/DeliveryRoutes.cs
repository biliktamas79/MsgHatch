using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Management;
using MsgHatch.Domain.Persistence;
using MsgHatch.Domain.Routing;
using static MsgHatch.AspNetCore.DeliveryRequestValues;

namespace MsgHatch.AspNetCore;

/// <summary>
/// The routes of the two route groups of the delivery management (section 4.19). Each route takes the optional query
/// parameter <c>tenantId</c> and uses the manager of that tenant key, except the pause, resume, and purge routes: they
/// act on all tenants, so a <c>tenantId</c> gives 400.
/// </summary>
internal static class DeliveryRoutes
{
    private static readonly DeliveryEndpointJson _json = DeliveryEndpointJson.Default;

    public static void MapOverviewTo(RouteGroupBuilder group)
    {
        group.MapGet("/channels", (Delegate)GetChannelsAsync);
        group.MapPost("/channels/{channel}/pause", PauseAsync);
        group.MapPost("/channels/{channel}/resume", ResumeAsync);
        group.MapGet("/traffic", (Delegate)GetTrafficAsync);
        group.MapGet("/inbox", (Delegate)GetReceivedMessagesAsync);
        group.MapPost("/inbox/retry", (Delegate)RetryReceivedAsync);
        group.MapPost("/inbox/cancel", (Delegate)CancelReceivedAsync);
        group.MapGet("/outbox", (Delegate)GetOutgoingMessagesAsync);
        group.MapGet("/outbox/{id:guid}", GetOutgoingMessageAsync);
        group.MapPost("/outbox/retry", (Delegate)RetryOutgoingAsync);
        group.MapPost("/outbox/cancel", (Delegate)CancelOutgoingAsync);
        group.MapPost("/purge", (Delegate)PurgeAsync);
    }

    public static void MapContentTo(RouteGroupBuilder group)
    {
        group.MapGet("/inbox/{id:guid}/content", GetReceivedContentAsync);
        group.MapGet("/outbox/{id:guid}/content", GetOutgoingContentAsync);
    }

    private static Task<IResult> GetChannelsAsync(HttpContext context) =>
        RunAsync(context, async manager => Json(
            await manager.GetChannelsAsync(context.RequestAborted),
            static channels => (IReadOnlyList<ChannelStatusResponse>)[.. channels.Select(ChannelStatusResponse.From)],
            _json.IReadOnlyListChannelStatusResponse));

    private static Task<IResult> PauseAsync(HttpContext context, string channel) =>
        RunGlobalAsync(context, manager => WithBodyAsync(context, _json.PauseRequest, async body =>
        {
            var id = ChannelOf(channel);
            return id.IsError ? Problem(id.Error!) : NoContent(await manager.PauseChannelAsync(id.Value, body.Note, context.RequestAborted));
        }));

    private static Task<IResult> ResumeAsync(HttpContext context, string channel) =>
        RunGlobalAsync(context, async manager =>
        {
            var id = ChannelOf(channel);
            return id.IsError ? Problem(id.Error!) : NoContent(await manager.ResumeChannelAsync(id.Value, context.RequestAborted));
        });

    private static Task<IResult> GetTrafficAsync(HttpContext context) =>
        RunAsync(context, async manager =>
        {
            var query = TrafficQueryOf(context.Request.Query);
            return query.IsError ? Problem(query.Error!) : Json(
                await manager.GetTrafficAsync(query.Value, context.RequestAborted), TrafficReportResponse.From, _json.TrafficReportResponse);
        });

    private static Task<IResult> GetReceivedMessagesAsync(HttpContext context) =>
        RunAsync(context, async manager =>
        {
            var query = InboxQueryOf(context.Request.Query);
            return query.IsError ? Problem(query.Error!) : Json(
                await manager.GetReceivedMessagesAsync(query.Value, context.RequestAborted),
                static page => PageOf(page, ReceivedMessageSummaryResponse.From),
                _json.MessagePageResponseReceivedMessageSummaryResponse);
        });

    private static Task<IResult> GetOutgoingMessagesAsync(HttpContext context) =>
        RunAsync(context, async manager =>
        {
            var query = OutboxQueryOf(context.Request.Query);
            return query.IsError ? Problem(query.Error!) : Json(
                await manager.GetOutgoingMessagesAsync(query.Value, context.RequestAborted),
                static page => PageOf(page, OutgoingMessageSummaryResponse.From),
                _json.MessagePageResponseOutgoingMessageSummaryResponse);
        });

    private static Task<IResult> GetOutgoingMessageAsync(HttpContext context, Guid id) =>
        RunAsync(context, async manager =>
        {
            var registry = context.RequestServices.GetRequiredService<IContentTypeRegistry>();
            return Json(
                await manager.GetOutgoingMessageAsync(id, context.RequestAborted),
                detail => new OutgoingMessageDetailResponse(
                    OutgoingMessageSummaryResponse.From(detail.Summary), TraceJsonOf(registry, detail.Trace), detail.Tries),
                _json.OutgoingMessageDetailResponse);
        });

    private static Task<IResult> RetryReceivedAsync(HttpContext context) =>
        ActOnInboxAsync(context, static (manager, query, ct) => manager.RetryReceivedAsync(query, ct));

    private static Task<IResult> CancelReceivedAsync(HttpContext context) =>
        ActOnInboxAsync(context, static (manager, query, ct) => manager.CancelReceivedAsync(query, ct));

    private static Task<IResult> RetryOutgoingAsync(HttpContext context) =>
        ActOnOutboxAsync(context, static (manager, query, ct) => manager.RetryOutgoingAsync(query, ct));

    private static Task<IResult> CancelOutgoingAsync(HttpContext context) =>
        ActOnOutboxAsync(context, static (manager, query, ct) => manager.CancelOutgoingAsync(query, ct));

    private static Task<IResult> PurgeAsync(HttpContext context) =>
        RunGlobalAsync(context, manager => WithBodyAsync(context, _json.PurgeRequest, async body => Json(
            await manager.PurgeAsync(Utc(body.FinishedBefore), body.MaxCount, context.RequestAborted),
            static deleted => deleted,
            _json.DeletedMessages)));

    private static Task<IResult> GetReceivedContentAsync(HttpContext context, Guid id) =>
        RunAsync(context, async manager =>
        {
            var message = await manager.GetReceivedContentAsync(id, context.RequestAborted);
            return message.IsError ? Problem(message.Error!) : Content(context.RequestServices.GetRequiredService<MsgHatchJson>().Serialize(message.Value));
        });

    private static Task<IResult> GetOutgoingContentAsync(HttpContext context, Guid id) =>
        RunAsync(context, async manager =>
        {
            var message = await manager.GetOutgoingContentAsync(id, context.RequestAborted);
            return message.IsError ? Problem(message.Error!) : Content(context.RequestServices.GetRequiredService<MsgHatchJson>().Serialize(message.Value));
        });

    private static Task<IResult> ActOnInboxAsync(
        HttpContext context, Func<IDeliveryManager, InboxQuery, CancellationToken, ValueTask<ErrorOr<DeliveryActionResult>>> action) =>
        RunAsync(context, manager => WithBodyAsync(context, _json.InboxFilterRequest, async body =>
        {
            var query = InboxQueryOf(body);
            return query.IsError ? Problem(query.Error!) : Json(
                await action(manager, query.Value, context.RequestAborted), static result => result, _json.DeliveryActionResult);
        }));

    private static Task<IResult> ActOnOutboxAsync(
        HttpContext context, Func<IDeliveryManager, OutboxQuery, CancellationToken, ValueTask<ErrorOr<DeliveryActionResult>>> action) =>
        RunAsync(context, manager => WithBodyAsync(context, _json.OutboxFilterRequest, async body =>
        {
            var query = OutboxQueryOf(body);
            return query.IsError ? Problem(query.Error!) : Json(
                await action(manager, query.Value, context.RequestAborted), static result => result, _json.DeliveryActionResult);
        }));

    private static async Task<IResult> RunAsync(HttpContext context, Func<IDeliveryManager, Task<IResult>> action)
    {
        var tenant = TenantOf(context.Request.Query["tenantId"]);
        return tenant.IsError
            ? Problem(tenant.Error!)
            : await action(context.RequestServices.GetRequiredKeyedService<IDeliveryManager>(TenantKey.For(tenant.Value)));
    }

    /// <summary>Runs a route that acts on all tenants, so it uses the manager of <see cref="TenantKey.None"/>, and a <c>tenantId</c> gives 400.</summary>
    private static Task<IResult> RunGlobalAsync(HttpContext context, Func<IDeliveryManager, Task<IResult>> action) =>
        context.Request.Query.ContainsKey("tenantId")
            ? Task.FromResult<IResult>(Problem(new Error(Errors.ValidationFailed, "This route acts on all tenants, so it takes no tenantId.")))
            : action(context.RequestServices.GetRequiredService<IDeliveryManager>());

    private static MessagePageResponse<TResponse> PageOf<TSummary, TResponse>(MessagePage<TSummary> page, Func<TSummary, TResponse> response) =>
        new([.. page.Items.Select(response)], page.Next is { } next ? CursorText(next) : null);

    /// <summary>Writes a route trace with the JSON type information of its newest registered version, as <see cref="MsgHatchJson"/> does.</summary>
    private static JsonElement? TraceJsonOf(IContentTypeRegistry registry, RouteTrace? trace)
    {
        if (trace is null)
        {
            return null;
        }

        var type = trace.GetType();
        var entry = registry.Entries.Where(entry => entry.ClrType == type).MaxBy(static entry => entry.Version)
            ?? throw new InvalidOperationException($"{type.Name} is not a registered trace type.");
        return JsonSerializer.SerializeToElement(trace, entry.ContentJsonTypeInfo);
    }

    /// <summary>
    /// Reads the JSON body, and runs the action with it. A content type that is not JSON gives 415 and calls no
    /// manager, so a form of another site cannot post a body as text (D156). Invalid JSON gives 400.
    /// </summary>
    private static async Task<IResult> WithBodyAsync<T>(HttpContext context, JsonTypeInfo<T> typeInfo, Func<T, Task<IResult>> action)
        where T : class
    {
        if (!context.Request.HasJsonContentType())
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status415UnsupportedMediaType);
        }

        var body = await ReadAsync(context, typeInfo);
        return body.IsError ? Problem(body.Error!) : await action(body.Value);
    }

    private static async Task<ErrorOr<T>> ReadAsync<T>(HttpContext context, JsonTypeInfo<T> typeInfo)
        where T : class
    {
        try
        {
            return await context.Request.ReadFromJsonAsync(typeInfo, context.RequestAborted) is { } request
                ? request
                : new Error(Errors.ValidationFailed, "The body is the JSON null.");
        }
        catch (JsonException exception)
        {
            return new Error(Errors.ValidationFailed, exception.Message);
        }
    }

    private static IResult Json<T, TResponse>(ErrorOr<T> result, Func<T, TResponse> toResponse, JsonTypeInfo<TResponse> typeInfo) =>
        result.IsError ? Problem(result.Error!) : TypedResults.Json(toResponse(result.Value), typeInfo);

    private static IResult Content(ErrorOr<byte[]> json) =>
        json.IsError ? Problem(json.Error!) : TypedResults.Bytes(json.Value, "application/json");

    private static IResult NoContent(Error? error) => error is null ? TypedResults.NoContent() : Problem(error);

    private static ProblemHttpResult Problem(Error error) =>
        TypedResults.Problem(detail: error.Message, statusCode: StatusCodeOf(error.Type), title: error.Type.Name);

    private static int StatusCodeOf(ErrorType type) =>
        type == Errors.ValidationFailed ? StatusCodes.Status400BadRequest
        : type == Errors.NotFound ? StatusCodes.Status404NotFound
        : StatusCodes.Status500InternalServerError;
}
