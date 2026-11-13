using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Routing;
using static MsgHatch.AspNetCore.DeliveryRequestValues;

namespace MsgHatch.AspNetCore;

/// <summary>
/// The routes of the registry administration (section 4.20). The GET routes join <see cref="IMessageRegistry"/> with
/// the revisions of <see cref="IRegistryWriter.GetEntriesAsync"/>, or give the revision 0 with no writer.
/// </summary>
internal static class RegistryRoutes
{
    private static readonly RegistryEndpointJson _json = RegistryEndpointJson.Default;

    public static void MapTo(RouteGroupBuilder group, bool writable)
    {
        group.MapGet("/channels", (Delegate)GetChannelsAsync);
        group.MapGet("/message-types", (Delegate)GetMessageTypesAsync);
        if (writable)
        {
            group.MapPut("/channels/{channel}", SaveChannelAsync);
            group.MapDelete("/channels/{channel}", ResetChannelAsync);
            group.MapPut("/message-types/{type}", SaveMessageTypeAsync);
            group.MapDelete("/message-types/{type}", ResetMessageTypeAsync);
        }
    }

    private static async Task<IResult> GetChannelsAsync(HttpContext context)
    {
        var revisions = await RevisionsAsync(context);
        var registry = context.RequestServices.GetRequiredService<IMessageRegistry>();
        return revisions.IsError ? Problem(revisions.Error!) : TypedResults.Json<IReadOnlyList<RegistryChannelResponse>>(
            [.. registry.Channels.Select(channel => RegistryChannelResponse.From(channel, revisions.Value.Channels.GetValueOrDefault(channel.Id)))],
            _json.IReadOnlyListRegistryChannelResponse);
    }

    private static async Task<IResult> GetMessageTypesAsync(HttpContext context)
    {
        var revisions = await RevisionsAsync(context);
        var registry = context.RequestServices.GetRequiredService<IMessageRegistry>();
        return revisions.IsError ? Problem(revisions.Error!) : TypedResults.Json<IReadOnlyList<RegistryMessageTypeResponse>>(
            [.. registry.MessageTypes.Select(type => RegistryMessageTypeResponse.From(type, revisions.Value.MessageTypes.GetValueOrDefault(type.Type)))],
            _json.IReadOnlyListRegistryMessageTypeResponse);
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

    private static async Task<ErrorOr<Revisions>> RevisionsAsync(HttpContext context)
    {
        if (context.RequestServices.GetService<IRegistryWriter>() is not { } writer)
        {
            return new Revisions([], []);
        }

        var entries = await writer.GetEntriesAsync(context.RequestAborted);
        return entries.IsError ? entries.Error : new Revisions(
            entries.Value.Channels.DistinctBy(static entry => entry.Channel).ToDictionary(static entry => entry.Channel, static entry => entry.Revision),
            entries.Value.MessageTypes.DistinctBy(static entry => entry.Type).ToDictionary(static entry => entry.Type, static entry => entry.Revision));
    }

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

    private sealed record Revisions(Dictionary<ChannelId, int> Channels, Dictionary<MessageType, int> MessageTypes);
}
