using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Templates.Management;

namespace MsgHatch.Templates.AspNetCore;

/// <summary>
/// The routes of the template administration (section 4.22). An endpoint checks only the values that need a type: the
/// message type, the kind, and the tenant id. The queries and the commands validate the language, the part, and the company id (D222, D231).
/// </summary>
internal static class TemplateRoutes
{
    private const string _templatePath = "/{messageType}/{language}/{part}/{kind}";

    private static readonly TemplateEndpointJson _json = TemplateEndpointJson.Default;

    public static void MapTo(RouteGroupBuilder group)
    {
        group.MapGet(string.Empty, GetTemplatesAsync);
        group.MapGet("/parts", GetTemplatePartsAsync);
        group.MapGet(_templatePath, GetTemplateAsync);
        group.MapPut(_templatePath, SaveTemplateAsync);
        group.MapDelete(_templatePath, DeleteTemplateAsync);
        group.MapPost(_templatePath + "/check", CheckTemplateAsync);
    }

    private static Task<IResult> GetTemplatesAsync(HttpContext context, string? messageType, string? companyId) =>
        RunAsync(context, async (ITemplateQueries queries) =>
        {
            var type = Optional(messageType, MessageTypeOf);
            return type.IsError ? Problem(type.Error) : Json(
                await queries.GetTemplatesAsync(new TemplateQuery(type.Value, companyId), context.RequestAborted),
                static entries => [.. entries.Select(TemplateEntryResponse.From)],
                _json.IReadOnlyListTemplateEntryResponse);
        });

    private static Task<IResult> GetTemplatePartsAsync(HttpContext context, string? messageType) =>
        RunAsync(context, async (ITemplateQueries queries) =>
        {
            var type = Optional(messageType, MessageTypeOf);
            return type.IsError ? Problem(type.Error) : Json(
                await queries.GetTemplatePartsAsync(type.Value, context.RequestAborted),
                static parts => [.. parts.Select(TemplatePartResponse.From)],
                _json.IReadOnlyListTemplatePartResponse);
        });

    private static Task<IResult> GetTemplateAsync(
        HttpContext context, string messageType, string language, string part, string kind, string? companyId) =>
        RunAsync(context, async (ITemplateQueries queries) =>
        {
            var lookup = LookupOf(messageType, language, part, kind, companyId);
            return lookup.IsError ? Problem(lookup.Error) : Json(
                await queries.GetTemplateAsync(lookup.Value, context.RequestAborted),
                source => TemplateResponse.From(lookup.Value, source),
                _json.TemplateResponse);
        });

    private static Task<IResult> SaveTemplateAsync(
        HttpContext context, string messageType, string language, string part, string kind, string? companyId) =>
        !context.Request.HasJsonContentType()
            ? Task.FromResult<IResult>(TypedResults.Problem(statusCode: StatusCodes.Status415UnsupportedMediaType))
            : RunAsync(context, async (ITemplateCommands commands) =>
            {
                var lookup = LookupOf(messageType, language, part, kind, companyId);
                if (lookup.IsError)
                {
                    return Problem(lookup.Error);
                }

                var body = await ReadAsync(context, _json.TemplateSaveRequest);
                return body.IsError ? Problem(body.Error) : Json(
                    await commands.SaveTemplateAsync(lookup.Value, body.Value.Text, body.Value.Version, context.RequestAborted),
                    TemplateEntryResponse.From,
                    _json.TemplateEntryResponse);
            });

    private static Task<IResult> CheckTemplateAsync(
        HttpContext context, string messageType, string language, string part, string kind, string? companyId) =>
        !context.Request.HasJsonContentType()
            ? Task.FromResult<IResult>(TypedResults.Problem(statusCode: StatusCodes.Status415UnsupportedMediaType))
            : RunAsync(context, async (ITemplateQueries queries) =>
            {
                var lookup = LookupOf(messageType, language, part, kind, companyId);
                if (lookup.IsError)
                {
                    return Problem(lookup.Error);
                }

                var body = await ReadAsync(context, _json.TemplateCheckRequest);
                return body.IsError ? Problem(body.Error) : Json(
                    await queries.CheckTemplateAsync(lookup.Value, body.Value.Text, context.RequestAborted),
                    static issues => [.. issues.Select(TemplateIssueResponse.From)],
                    _json.IReadOnlyListTemplateIssueResponse);
            });

    private static Task<IResult> DeleteTemplateAsync(
        HttpContext context, string messageType, string language, string part, string kind, string? companyId) =>
        RunAsync(context, async (ITemplateCommands commands) =>
        {
            var lookup = LookupOf(messageType, language, part, kind, companyId);
            if (lookup.IsError)
            {
                return Problem(lookup.Error);
            }

            var error = await commands.DeleteTemplateAsync(lookup.Value, context.RequestAborted);
            return error is null ? TypedResults.NoContent() : Problem(error);
        });

    private static async Task<IResult> RunAsync<TService>(HttpContext context, Func<TService, Task<IResult>> action)
        where TService : notnull
    {
        var tenant = Optional(context.Request.Query["tenantId"], TenantId.Create);
        return tenant.IsError
            ? Problem(tenant.Error)
            : await action(context.RequestServices.GetRequiredKeyedService<TService>(TenantKey.For(tenant.Value)));
    }

    private static ErrorOr<T?> Optional<T>(string? value, Func<string, ErrorOr<T>> parse)
        where T : struct
    {
        if (value is null)
        {
            return (T?)null;
        }

        var parsed = parse(value);
        return parsed.IsError ? parsed.Error : parsed.Value;
    }

    private static ErrorOr<TemplateLookup> LookupOf(string messageType, string language, string part, string kind, string? companyId)
    {
        var type = MessageTypeOf(messageType);
        var partKind = KindOf(kind);
        var error = type.Error ?? partKind.Error;
        return error is not null ? error : new TemplateLookup(type.Value, language, companyId, part, partKind.Value);
    }

    private static ErrorOr<MessageType> MessageTypeOf(string name)
    {
        try
        {
            return new MessageType(name);
        }
        catch (ArgumentException exception)
        {
            return new Error(Errors.ValidationFailed, exception.Message);
        }
    }

    private static ErrorOr<TemplatePartKind> KindOf(string kind) =>
        Enum.GetNames<TemplatePartKind>().Contains(kind, StringComparer.Ordinal)
            ? Enum.Parse<TemplatePartKind>(kind)
            : new Error(Errors.ValidationFailed, $"The kind {kind} is not PlainText or Html.");

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
        TypedResults.Problem(detail: error.Message, statusCode: StatusCodeOf(error.Type), title: error.Type.Name);

    private static int StatusCodeOf(ErrorType type) =>
        type == Errors.ValidationFailed ? StatusCodes.Status400BadRequest
        : type == Errors.NotFound ? StatusCodes.Status404NotFound
        : type == Errors.Conflict ? StatusCodes.Status409Conflict
        : type == Errors.TemplateInvalid ? StatusCodes.Status422UnprocessableEntity
        : StatusCodes.Status500InternalServerError;
}
