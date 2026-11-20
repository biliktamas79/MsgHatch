using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Api.Contracts;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Management;
using static MsgHatch.AspNetCore.DeliveryRequestValues;

namespace MsgHatch.AspNetCore;

/// <summary>
/// The routes of the two route groups of the delivery management (section 4.19). Each route takes the optional query
/// parameter <c>tenantId</c> and calls <see cref="IDeliveryQueries"/> or <see cref="IDeliveryCommands"/> of that tenant key
/// (D289), except the pause, resume, and purge routes: they act on all tenants, so a <c>tenantId</c> gives 400.
/// </summary>
internal static class DeliveryRoutes
{
    private static readonly DeliveryEndpointJson _json = DeliveryEndpointJson.Default;

    public static void MapOverviewTo(RouteGroupBuilder group)
    {
        group.MapGet(DeliveryApiRoutes.Channels, (Delegate)GetChannelsAsync);
        group.MapPost(DeliveryApiRoutes.ChannelPause, PauseAsync);
        group.MapPost(DeliveryApiRoutes.ChannelResume, ResumeAsync);
        group.MapGet(DeliveryApiRoutes.Traffic, (Delegate)GetTrafficAsync);
        group.MapGet(DeliveryApiRoutes.Inbox, (Delegate)GetReceivedMessagesAsync);
        group.MapPost(DeliveryApiRoutes.InboxRetry, (Delegate)RetryReceivedAsync);
        group.MapPost(DeliveryApiRoutes.InboxCancel, (Delegate)CancelReceivedAsync);
        group.MapGet(DeliveryApiRoutes.Outbox, (Delegate)GetOutgoingMessagesAsync);
        group.MapGet(DeliveryApiRoutes.OutgoingMessage, GetOutgoingMessageAsync);
        group.MapPost(DeliveryApiRoutes.OutboxRetry, (Delegate)RetryOutgoingAsync);
        group.MapPost(DeliveryApiRoutes.OutboxCancel, (Delegate)CancelOutgoingAsync);
        group.MapPost(DeliveryApiRoutes.Purge, (Delegate)PurgeAsync);
    }

    public static void MapContentTo(RouteGroupBuilder group)
    {
        group.MapGet(DeliveryApiRoutes.ReceivedContent, GetReceivedContentAsync);
        group.MapGet(DeliveryApiRoutes.OutgoingContent, GetOutgoingContentAsync);
    }

    private static Task<IResult> GetChannelsAsync(HttpContext context) =>
        RunQueryAsync(context, async queries => Json(
            await queries.GetChannelsAsync(context.RequestAborted),
            static channels => (IReadOnlyList<ChannelStatusResponse>)[.. channels.Select(ChannelStatusResponse.From)],
            _json.IReadOnlyListChannelStatusResponse));

    private static Task<IResult> PauseAsync(HttpContext context, string channel) =>
        RunGlobalAsync(context, commands => WithBodyAsync(context, _json.PauseRequest, async body =>
        {
            var id = ChannelOf(channel);
            return id.IsError ? Problem(id.Error!) : NoContent(await commands.PauseChannelAsync(id.Value, body.Note, context.RequestAborted));
        }));

    private static Task<IResult> ResumeAsync(HttpContext context, string channel) =>
        RunGlobalAsync(context, async commands =>
        {
            var id = ChannelOf(channel);
            return id.IsError ? Problem(id.Error!) : NoContent(await commands.ResumeChannelAsync(id.Value, context.RequestAborted));
        });

    private static Task<IResult> GetTrafficAsync(HttpContext context) =>
        RunQueryAsync(context, async queries =>
        {
            var query = TrafficQueryOf(context.Request.Query);
            return query.IsError ? Problem(query.Error!) : Json(
                await queries.GetTrafficAsync(query.Value, context.RequestAborted), TrafficReportResponse.From, _json.TrafficReportResponse);
        });

    private static Task<IResult> GetReceivedMessagesAsync(HttpContext context) =>
        RunQueryAsync(context, async queries =>
        {
            var query = InboxQueryOf(context.Request.Query);
            return query.IsError ? Problem(query.Error!) : Json(
                await queries.GetReceivedMessagesAsync(query.Value, context.RequestAborted),
                static page => PageOf(page, ReceivedMessageSummaryResponse.From),
                _json.MessagePageResponseReceivedMessageSummaryResponse);
        });

    private static Task<IResult> GetOutgoingMessagesAsync(HttpContext context) =>
        RunQueryAsync(context, async queries =>
        {
            var query = OutboxQueryOf(context.Request.Query);
            return query.IsError ? Problem(query.Error!) : Json(
                await queries.GetOutgoingMessagesAsync(query.Value, context.RequestAborted),
                static page => PageOf(page, OutgoingMessageSummaryResponse.From),
                _json.MessagePageResponseOutgoingMessageSummaryResponse);
        });

    private static Task<IResult> GetOutgoingMessageAsync(HttpContext context, Guid id) =>
        RunQueryAsync(context, async queries => Json(
            await queries.GetOutgoingMessageAsync(id, context.RequestAborted), OutgoingMessageDetailResponse.From, _json.OutgoingMessageDetailResponse));

    private static Task<IResult> RetryReceivedAsync(HttpContext context) =>
        ActOnInboxAsync(context, static (commands, query, ct) => commands.RetryReceivedAsync(query, ct));

    private static Task<IResult> CancelReceivedAsync(HttpContext context) =>
        ActOnInboxAsync(context, static (commands, query, ct) => commands.CancelReceivedAsync(query, ct));

    private static Task<IResult> RetryOutgoingAsync(HttpContext context) =>
        ActOnOutboxAsync(context, static (commands, query, ct) => commands.RetryOutgoingAsync(query, ct));

    private static Task<IResult> CancelOutgoingAsync(HttpContext context) =>
        ActOnOutboxAsync(context, static (commands, query, ct) => commands.CancelOutgoingAsync(query, ct));

    private static Task<IResult> PurgeAsync(HttpContext context) =>
        RunGlobalAsync(context, commands => WithBodyAsync(context, _json.PurgeRequest, async body => Json(
            await commands.PurgeAsync(Utc(body.FinishedBefore), body.MaxCount, context.RequestAborted),
            static deleted => deleted,
            _json.DeletedMessages)));

    private static Task<IResult> GetReceivedContentAsync(HttpContext context, Guid id) =>
        RunQueryAsync(context, async queries => Content(await queries.GetReceivedContentAsync(id, context.RequestAborted)));

    private static Task<IResult> GetOutgoingContentAsync(HttpContext context, Guid id) =>
        RunQueryAsync(context, async queries => Content(await queries.GetOutgoingContentAsync(id, context.RequestAborted)));

    private static Task<IResult> ActOnInboxAsync(
        HttpContext context, Func<IDeliveryCommands, InboxQuery, CancellationToken, ValueTask<ErrorOr<DeliveryActionResult>>> action) =>
        RunCommandAsync(context, commands => WithBodyAsync(context, _json.InboxFilterRequest, async body =>
        {
            var query = InboxQueryOf(body);
            return query.IsError ? Problem(query.Error!) : Json(
                await action(commands, query.Value, context.RequestAborted), static result => result, _json.DeliveryActionResult);
        }));

    private static Task<IResult> ActOnOutboxAsync(
        HttpContext context, Func<IDeliveryCommands, OutboxQuery, CancellationToken, ValueTask<ErrorOr<DeliveryActionResult>>> action) =>
        RunCommandAsync(context, commands => WithBodyAsync(context, _json.OutboxFilterRequest, async body =>
        {
            var query = OutboxQueryOf(body);
            return query.IsError ? Problem(query.Error!) : Json(
                await action(commands, query.Value, context.RequestAborted), static result => result, _json.DeliveryActionResult);
        }));

    private static Task<IResult> RunQueryAsync(HttpContext context, Func<IDeliveryQueries, Task<IResult>> action) =>
        RunAsync(context, action);

    private static Task<IResult> RunCommandAsync(HttpContext context, Func<IDeliveryCommands, Task<IResult>> action) =>
        RunAsync(context, action);

    private static async Task<IResult> RunAsync<TService>(HttpContext context, Func<TService, Task<IResult>> action)
        where TService : notnull
    {
        var tenant = TenantOf(context.Request.Query[DeliveryApiRoutes.Query.TenantId]);
        return tenant.IsError
            ? Problem(tenant.Error!)
            : await action(context.RequestServices.GetRequiredKeyedService<TService>(TenantKey.For(tenant.Value)));
    }

    /// <summary>Runs a route that acts on all tenants, so it uses the commands of <see cref="TenantKey.None"/>, and a <c>tenantId</c> gives 400.</summary>
    private static Task<IResult> RunGlobalAsync(HttpContext context, Func<IDeliveryCommands, Task<IResult>> action) =>
        context.Request.Query.ContainsKey(DeliveryApiRoutes.Query.TenantId)
            ? Task.FromResult<IResult>(Problem(new Error(Errors.ValidationFailed, "This route acts on all tenants, so it takes no tenantId.")))
            : action(context.RequestServices.GetRequiredKeyedService<IDeliveryCommands>(TenantKey.None));

    private static MessagePageResponse<TResponse> PageOf<TSummary, TResponse>(MessagePage<TSummary> page, Func<TSummary, TResponse> response) =>
        new([.. page.Items.Select(response)], page.Next is { } next ? DeliveryApiRoutes.CursorText(next) : null);

    /// <summary>
    /// Reads the JSON body, and runs the action with it. A content type that is not JSON gives 415 and calls no
    /// manager, so a form of another site cannot post a body as text (D156). Invalid JSON gives 400, written with
    /// <paramref name="problem"/>, or with the problem of the delivery routes when it is null.
    /// </summary>
    public static async Task<IResult> WithBodyAsync<T>(
        HttpContext context, JsonTypeInfo<T> typeInfo, Func<T, Task<IResult>> action, Func<Error, IResult>? problem = null)
        where T : class
    {
        if (!context.Request.HasJsonContentType())
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status415UnsupportedMediaType);
        }

        var body = await ReadAsync(context, typeInfo);
        return body.IsError ? (problem ?? Problem)(body.Error!) : await action(body.Value);
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

    private static IResult Content(ErrorOr<MessageContent> content) =>
        content.IsError ? Problem(content.Error!) : TypedResults.Text(content.Value.Json, "application/json", Encoding.UTF8);

    private static IResult NoContent(Error? error) => error is null ? TypedResults.NoContent() : Problem(error);

    private static ProblemHttpResult Problem(Error error) =>
        TypedResults.Problem(detail: error.Message, statusCode: StatusCodeOf(error.Type), title: error.Type.Name);

    private static int StatusCodeOf(ErrorType type) =>
        type == Errors.ValidationFailed ? StatusCodes.Status400BadRequest
        : type == Errors.NotFound ? StatusCodes.Status404NotFound
        : StatusCodes.Status500InternalServerError;
}
