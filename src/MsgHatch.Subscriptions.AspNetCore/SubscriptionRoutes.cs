using System.Security.Claims;
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
using MsgHatch.Domain.Persistence;
using MsgHatch.Subscriptions.Api.Contracts;
using MsgHatch.Subscriptions.Management;
using MsgHatch.Subscriptions.Verification;

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
        group.MapGet(SubscriptionApiRoutes.Channels, (Delegate)GetChannelsAsync);
        group.MapGet(SubscriptionApiRoutes.MessageTypes, GetMessageTypesAsync);
        group.MapGet(SubscriptionApiRoutes.Subscriptions, GetSubscriptionsAsync);
        group.MapGet(SubscriptionApiRoutes.Subscription, GetSubscriptionAsync);
        group.MapPost(SubscriptionApiRoutes.Subscriptions, (Delegate)CreateAsync);
        group.MapPut(SubscriptionApiRoutes.Subscription, UpdateAsync);
        group.MapDelete(SubscriptionApiRoutes.Subscription, DeleteAsync);
    }

    /// <summary>Maps <c>POST /verification</c>, only on the group with no <c>/me</c> (D163).</summary>
    public static void MapVerificationTo(RouteGroupBuilder group) => group.MapPost(SubscriptionApiRoutes.Verification, (Delegate)VerifyAsync);

    private static async Task<IResult> VerifyAsync(HttpContext context)
    {
        if (!context.Request.HasJsonContentType())
        {
            return UnsupportedMediaType();
        }

        var tenant = RequestValues.Optional(context.Request.Query["tenantId"], TenantId.Create);
        var request = RequestValues.VerificationRequestOf(await ReadAsync(context, _json.RoutingVerificationBody));
        var error = tenant.Error ?? request.Error;
        if (error is not null)
        {
            return Problem(error);
        }

        var verifier = context.RequestServices.GetRequiredKeyedService<IRoutingVerifier>(TenantKey.For(tenant.Value));
        var registry = context.RequestServices.GetRequiredService<IContentTypeRegistry>();
        return Json(
            await verifier.VerifyAsync(request.Value, context.RequestAborted),
            report => RoutingVerificationResponse.From(report, registry),
            _json.RoutingVerificationResponse);
    }

    private Task<IResult> GetChannelsAsync(HttpContext context) =>
        RunAsync(context, async caller => Json(
            await caller.Queries.GetChannelsAsync(context.RequestAborted),
            static channels => [.. channels.Select(ChannelResponse.From)],
            _json.IReadOnlyListChannelResponse));

    private Task<IResult> GetMessageTypesAsync(HttpContext context, string? messageType, string? companyId, string? userId) =>
        RunAsync(context, async caller =>
        {
            var query = caller.MessageTypeQueryOf(messageType, companyId, userId);
            return query.IsError ? Problem(query.Error) : Json(
                await caller.Queries.GetMessageTypesAsync(query.Value, context.RequestAborted),
                states => [.. states.Select(state => MessageTypeResponse.From(state, caller.Registry))],
                _json.IReadOnlyListMessageTypeResponse);
        });

    private Task<IResult> GetSubscriptionsAsync(
        HttpContext context, string? messageType, string? companyId, string? userId, string? level) =>
        RunAsync(context, async caller =>
        {
            var query = caller.SubscriptionQueryOf(messageType, companyId, userId, level);
            return query.IsError ? Problem(query.Error) : Json(
                await caller.Queries.GetSubscriptionsAsync(query.Value, context.RequestAborted),
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

            var draft = caller.DraftOf(await ReadAsync(context, _json.SubscriptionRequest));
            if (draft.IsError)
            {
                return Problem(draft.Error);
            }

            var created = await caller.Commands.CreateAsync(draft.Value, context.RequestAborted);
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

            var request = await ReadAsync(context, _json.SubscriptionRequest);
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
                await caller.Commands.UpdateAsync(id, revision, draft.Value, context.RequestAborted),
                subscription => SubscriptionResponse.From(subscription, caller.Registry),
                _json.SubscriptionResponse);
        });

    private Task<IResult> DeleteAsync(HttpContext context, Guid id) =>
        RunAsync(context, async caller =>
        {
            var error = await caller.OwnerErrorAsync(id, context.RequestAborted)
                ?? await caller.Commands.DeleteAsync(id, context.RequestAborted);
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
                : await action(new Caller(context.RequestServices, TenantKey.For(tenant.Value), registry, SignedInUser: null, tenant.Value));
        }

        return signedInUser(context.User) is { } user
            ? await action(new Caller(context.RequestServices, TenantKey.For(signedInTenant?.Invoke(context.User)), registry, user, QueryTenant: null))
            : TypedResults.Problem(statusCode: StatusCodes.Status403Forbidden);
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
        result.IsError ? Problem(result.Error) : TypedResults.Json(toResponse(result.Value), typeInfo);

    private static ProblemHttpResult Problem(Error error) =>
        TypedResults.Problem(
            detail: error.Message,
            statusCode: ErrorProblems.StatusCodeOf(error.Type),
            title: error.Type.Name,
            extensions: new Dictionary<string, object?> { ["errorType"] = ErrorTypeJsonOf(error.Type) });

    private static JsonElement ErrorTypeJsonOf(ErrorType type) =>
        JsonSerializer.SerializeToElement(ErrorTypeBody.From(type), ApiErrorJson.Default.ErrorTypeBody);

    private static ProblemHttpResult UnsupportedMediaType() => TypedResults.Problem(statusCode: StatusCodes.Status415UnsupportedMediaType);
}
