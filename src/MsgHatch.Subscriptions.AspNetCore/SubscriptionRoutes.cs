using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Persistence;
using MsgHatch.Subscriptions.Management;

namespace MsgHatch.Subscriptions.AspNetCore;

/// <summary>
/// The routes of one route group (section 4.17). With no signed-in user function, the routes serve each owner and take
/// the tenant from the query parameter <c>tenantId</c>. With it, they are the <c>/me</c> routes of the signed-in user (D154).
/// </summary>
internal sealed class SubscriptionRoutes(
    Func<ClaimsPrincipal, UserId?>? signedInUser, Func<ClaimsPrincipal, TenantId?>? signedInTenant)
{
    private static readonly SubscriptionEndpointJson _json = SubscriptionEndpointJson.Default;

    public void MapTo(RouteGroupBuilder group)
    {
        group.MapGet("/channels", (Delegate)GetChannelsAsync);
        group.MapGet("/message-types", GetMessageTypesAsync);
        group.MapGet(string.Empty, GetSubscriptionsAsync);
        group.MapGet("/{id:guid}", GetSubscriptionAsync);
        group.MapPost(string.Empty, (Delegate)CreateAsync);
        group.MapPut("/{id:guid}", UpdateAsync);
        group.MapDelete("/{id:guid}", DeleteAsync);
    }

    private Task<IResult> GetChannelsAsync(HttpContext context) =>
        RunAsync(context, async caller => Json(
            await caller.Manager.GetChannelsAsync(context.RequestAborted),
            static channels => [.. channels.Select(ChannelResponse.From)],
            _json.IReadOnlyListChannelResponse));

    private Task<IResult> GetMessageTypesAsync(HttpContext context, string? messageType, string? companyId, string? userId) =>
        RunAsync(context, async caller =>
        {
            var query = caller.MessageTypeQueryOf(messageType, companyId, userId);
            return query.IsError ? Problem(query.Error) : Json(
                await caller.Manager.GetMessageTypesAsync(query.Value, context.RequestAborted),
                states => [.. states.Select(state => MessageTypeResponse.From(state, caller.Registry))],
                _json.IReadOnlyListMessageTypeResponse);
        });

    private Task<IResult> GetSubscriptionsAsync(
        HttpContext context, string? messageType, string? companyId, string? userId, string? level) =>
        RunAsync(context, async caller =>
        {
            var query = caller.SubscriptionQueryOf(messageType, companyId, userId, level);
            return query.IsError ? Problem(query.Error) : Json(
                await caller.Manager.GetSubscriptionsAsync(query.Value, context.RequestAborted),
                subscriptions => [.. subscriptions.Select(subscription => SubscriptionResponse.From(subscription, caller.Registry))],
                _json.IReadOnlyListSubscriptionResponse);
        });

    private Task<IResult> GetSubscriptionAsync(HttpContext context, Guid id) =>
        RunAsync(context, async caller => Json(
            await caller.GetOwnedAsync(id, context.RequestAborted),
            subscription => SubscriptionResponse.From(subscription, caller.Registry),
            _json.SubscriptionResponse));

    private Task<IResult> CreateAsync(HttpContext context) =>
        RunAsync(context, async caller =>
        {
            if (!context.Request.HasJsonContentType())
            {
                return UnsupportedMediaType();
            }

            var draft = caller.DraftOf(await ReadRequestAsync(context));
            if (draft.IsError)
            {
                return Problem(draft.Error);
            }

            var created = await caller.Manager.CreateAsync(draft.Value, context.RequestAborted);
            if (created.IsError)
            {
                return Problem(created.Error);
            }

            context.Response.Headers.Location = caller.LocationOf(context.Request, created.Value.Id);
            return TypedResults.Json(
                SubscriptionResponse.From(created.Value, caller.Registry), _json.SubscriptionResponse, statusCode: StatusCodes.Status201Created);
        });

    private Task<IResult> UpdateAsync(HttpContext context, Guid id) =>
        RunAsync(context, async caller =>
        {
            if (!context.Request.HasJsonContentType())
            {
                return UnsupportedMediaType();
            }

            var request = await ReadRequestAsync(context);
            var draft = caller.DraftOf(request);
            if (draft.IsError)
            {
                return Problem(draft.Error);
            }

            if (request.Value.Revision is not { } revision)
            {
                return Problem(new Error(Errors.ValidationFailed, "An update needs the revision that the caller read."));
            }

            return await caller.OwnerErrorAsync(id, context.RequestAborted) is { } ownerError ? Problem(ownerError) : Json(
                await caller.Manager.UpdateAsync(id, revision, draft.Value, context.RequestAborted),
                subscription => SubscriptionResponse.From(subscription, caller.Registry),
                _json.SubscriptionResponse);
        });

    private Task<IResult> DeleteAsync(HttpContext context, Guid id) =>
        RunAsync(context, async caller =>
        {
            var error = await caller.OwnerErrorAsync(id, context.RequestAborted)
                ?? await caller.Manager.DeleteAsync(id, context.RequestAborted);
            return error is null ? TypedResults.NoContent() : Problem(error);
        });

    private async Task<IResult> RunAsync(HttpContext context, Func<Caller, Task<IResult>> action)
    {
        var registry = context.RequestServices.GetRequiredService<IContentTypeRegistry>();
        if (signedInUser is null)
        {
            var tenant = RequestValues.Optional(context.Request.Query["tenantId"], TenantId.Create);
            return tenant.IsError
                ? Problem(tenant.Error)
                : await action(new Caller(ManagerOf(context, tenant.Value), registry, SignedInUser: null, tenant.Value));
        }

        return signedInUser(context.User) is { } user
            ? await action(new Caller(ManagerOf(context, signedInTenant?.Invoke(context.User)), registry, user, QueryTenant: null))
            : TypedResults.Problem(statusCode: StatusCodes.Status403Forbidden);
    }

    private static ISubscriptionManager ManagerOf(HttpContext context, TenantId? tenant) =>
        context.RequestServices.GetRequiredKeyedService<ISubscriptionManager>(TenantKey.For(tenant));

    private static async Task<ErrorOr<SubscriptionRequest>> ReadRequestAsync(HttpContext context)
    {
        try
        {
            return await context.Request.ReadFromJsonAsync(_json.SubscriptionRequest, context.RequestAborted) is { } request
                ? request
                : new Error(Errors.ValidationFailed, "The body is the JSON null.");
        }
        catch (JsonException exception)
        {
            return new Error(Errors.ValidationFailed, exception.Message);
        }
    }

    private static IResult Json<T, TResponse>(ErrorOr<T> result, Func<T, TResponse> toResponse, JsonTypeInfo<TResponse> typeInfo) =>
        result.IsError ? Problem(result.Error) : TypedResults.Json(toResponse(result.Value), typeInfo);

    private static ProblemHttpResult Problem(Error error) =>
        TypedResults.Problem(detail: error.Message, statusCode: StatusCodeOf(error.Type), title: error.Type.Name);

    private static int StatusCodeOf(ErrorType type) =>
        type == Errors.ValidationFailed ? StatusCodes.Status400BadRequest
        : type == Errors.NotFound ? StatusCodes.Status404NotFound
        : type == Errors.Conflict ? StatusCodes.Status409Conflict
        : StatusCodes.Status500InternalServerError;

    private static ProblemHttpResult UnsupportedMediaType() => TypedResults.Problem(statusCode: StatusCodes.Status415UnsupportedMediaType);
}
