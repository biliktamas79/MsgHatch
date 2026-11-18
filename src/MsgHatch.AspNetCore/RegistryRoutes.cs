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
using MsgHatch.Domain.Routing;
using static MsgHatch.AspNetCore.DeliveryRequestValues;

namespace MsgHatch.AspNetCore;

/// <summary>
/// The routes of the registry administration (section 4.20). The GET routes call <see cref="IRegistryQueries"/> of
/// <see cref="TenantKey.None"/>, because the registry belongs to the whole system (D269, D270).
/// </summary>
internal static class RegistryRoutes
{
    private static readonly RegistryEndpointJson _json = RegistryEndpointJson.Default;

    public static void MapTo(RouteGroupBuilder group, bool writable)
    {
        group.MapGet(RegistryApiRoutes.Channels, (Delegate)GetChannelsAsync);
        group.MapGet(RegistryApiRoutes.MessageTypes, (Delegate)GetMessageTypesAsync);
        if (writable)
        {
            group.MapPut(RegistryApiRoutes.Channel, SaveChannelAsync);
            group.MapDelete(RegistryApiRoutes.Channel, ResetChannelAsync);
            group.MapPut(RegistryApiRoutes.MessageType, SaveMessageTypeAsync);
            group.MapDelete(RegistryApiRoutes.MessageType, ResetMessageTypeAsync);
        }
    }

    private static async Task<IResult> GetChannelsAsync(HttpContext context)
    {
        var channels = await Queries(context).GetChannelsAsync(context.RequestAborted);
        return channels.IsError ? Problem(channels.Error) : TypedResults.Json<IReadOnlyList<RegistryChannelResponse>>(
            [.. channels.Value.Select(RegistryChannelResponse.From)], _json.IReadOnlyListRegistryChannelResponse);
    }

    private static async Task<IResult> GetMessageTypesAsync(HttpContext context)
    {
        var types = await Queries(context).GetMessageTypesAsync(context.RequestAborted);
        return types.IsError ? Problem(types.Error) : TypedResults.Json<IReadOnlyList<RegistryMessageTypeResponse>>(
            [.. types.Value.Select(RegistryMessageTypeResponse.From)], _json.IReadOnlyListRegistryMessageTypeResponse);
    }

    private static Task<IResult> SaveChannelAsync(HttpContext context, string channel) =>
        DeliveryRoutes.WithBodyAsync(context, _json.ChannelEntryRequest, async body =>
        {
            var id = ChannelOf(channel);
            return id.IsError ? Problem(id.Error!) : Json(
                await Writer(context).SaveChannelAsync(
                    new ChannelEntry(id.Value, body.DisplayName, body.Description, body.Enabled, body.Revision, default), context.RequestAborted),
                ChannelEntryResponse.From, _json.ChannelEntryResponse);
        });

    private static Task<IResult> SaveMessageTypeAsync(HttpContext context, string type) =>
        DeliveryRoutes.WithBodyAsync(context, _json.MessageTypeEntryRequest, async body =>
        {
            var id = MessageTypeOf(type);
            var disabled = ChannelsOf(body.DisabledChannels ?? []);
            var invalid = id.Error ?? disabled.Error;
            return invalid is not null ? Problem(invalid) : Json(
                await Writer(context).SaveMessageTypeAsync(
                    new MessageTypeEntry(id.Value, body.DisplayName, body.Description, body.Enabled, disabled.Value, body.Revision, default),
                    context.RequestAborted),
                MessageTypeEntryResponse.From, _json.MessageTypeEntryResponse);
        });

    private static async Task<IResult> ResetChannelAsync(HttpContext context, string channel)
    {
        var id = ChannelOf(channel);
        return id.IsError ? Problem(id.Error!) : NoContent(await Writer(context).ResetChannelAsync(id.Value, context.RequestAborted));
    }

    private static async Task<IResult> ResetMessageTypeAsync(HttpContext context, string type)
    {
        var id = MessageTypeOf(type);
        return id.IsError ? Problem(id.Error!) : NoContent(await Writer(context).ResetMessageTypeAsync(id.Value, context.RequestAborted));
    }

    private static IRegistryWriter Writer(HttpContext context) => context.RequestServices.GetRequiredService<IRegistryWriter>();

    private static IRegistryQueries Queries(HttpContext context) => context.RequestServices.GetRequiredKeyedService<IRegistryQueries>(TenantKey.None);

    private static ErrorOr<IReadOnlyList<ChannelId>> ChannelsOf(IReadOnlyList<string> names)
    {
        var channels = new ChannelId[names.Count];
        for (var position = 0; position < names.Count; position++)
        {
            var channel = ChannelOf(names[position]);
            if (channel.IsError)
            {
                return channel.Error!;
            }

            channels[position] = channel.Value;
        }

        return channels;
    }

    private static IResult Json<T, TResponse>(ErrorOr<T> result, Func<T, TResponse> toResponse, JsonTypeInfo<TResponse> typeInfo) =>
        result.IsError ? Problem(result.Error!) : TypedResults.Json(toResponse(result.Value), typeInfo);

    private static IResult NoContent(Error? error) => error is null ? TypedResults.NoContent() : Problem(error);

    private static ProblemHttpResult Problem(Error error) =>
        TypedResults.Problem(detail: error.Message, statusCode: StatusCodeOf(error.Type), title: error.Type.Name);

    private static int StatusCodeOf(ErrorType type) =>
        type == Errors.ValidationFailed ? StatusCodes.Status400BadRequest
        : type == Errors.NotFound ? StatusCodes.Status404NotFound
        : type == Errors.Conflict ? StatusCodes.Status409Conflict
        : StatusCodes.Status500InternalServerError;
}
