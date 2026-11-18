using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Persistence;
using MsgHatch.Domain.Routing;
using MsgHatch.Subscriptions.Api.Contracts;
using MsgHatch.Subscriptions.Management;

namespace MsgHatch.Subscriptions.AspNetCore;

/// <summary>
/// The queries and the commands of the tenant of one request, and the signed-in user of a <c>/me</c> route (D231). A
/// <c>/me</c> route uses only the user-level subscriptions of that user, and gives the same 404 for an unknown id and for
/// another owner (D154).
/// </summary>
internal sealed record Caller(
    IServiceProvider RequestServices, TenantKey Key, IContentTypeRegistry Registry, UserId? SignedInUser, TenantId? QueryTenant)
{
    private static readonly Error _notStored = new(Errors.NotFound, "The subscription is not stored.");

    public ISubscriptionQueries Queries => RequestServices.GetRequiredKeyedService<ISubscriptionQueries>(Key);

    public ISubscriptionCommands Commands => RequestServices.GetRequiredKeyedService<ISubscriptionCommands>(Key);

    public ErrorOr<MessageTypeQuery> MessageTypeQueryOf(string? messageType, string? companyId, string? userId)
    {
        var type = RequestValues.Optional(messageType, RequestValues.MessageTypeOf);
        if (type.IsError)
        {
            return type.Error;
        }

        if (SignedInUser is { } user)
        {
            return new MessageTypeQuery(type.Value, User: user);
        }

        var company = RequestValues.Optional(companyId, CompanyId.Create);
        var owner = RequestValues.Optional(userId, UserId.Create);
        return company.Error ?? owner.Error ?? (ErrorOr<MessageTypeQuery>)new MessageTypeQuery(type.Value, company.Value, owner.Value);
    }

    public ErrorOr<SubscriptionQuery> SubscriptionQueryOf(string? messageType, string? companyId, string? userId, string? level)
    {
        var type = RequestValues.Optional(messageType, RequestValues.MessageTypeOf);
        if (type.IsError)
        {
            return type.Error;
        }

        if (SignedInUser is { } user)
        {
            return new SubscriptionQuery(type.Value, User: user, Level: SubscriptionLevel.User);
        }

        var company = RequestValues.Optional(companyId, CompanyId.Create);
        var owner = RequestValues.Optional(userId, UserId.Create);
        var ownerLevel = RequestValues.Optional(level, RequestValues.LevelOf);
        return company.Error ?? owner.Error ?? ownerLevel.Error
            ?? (ErrorOr<SubscriptionQuery>)new SubscriptionQuery(type.Value, company.Value, owner.Value, ownerLevel.Value);
    }

    public ErrorOr<SubscriptionDraft> DraftOf(ErrorOr<SubscriptionRequest> read)
    {
        if (read.IsError)
        {
            return read.Error;
        }

        var request = read.Value;
        var owner = SignedInUser is { } user ? SubscriptionOwner.ForUser(user) : RequestValues.OwnerOf(request);
        var type = RequestValues.MessageTypeOf(request.MessageType);
        var channel = RequestValues.Optional(request.Channel, RequestValues.ChannelOf);
        var recipients = RequestValues.RecipientsOf(request.Recipients);
        var error = owner.Error ?? type.Error ?? channel.Error ?? recipients.Error
            ?? (Enum.IsDefined(request.Kind) ? null : new Error(Errors.ValidationFailed, $"The kind {request.Kind} is not known."));
        if (error is not null)
        {
            return error;
        }

        var settings = SettingsOf(request, channel.Value);
        return settings.IsError
            ? settings.Error
            : new SubscriptionDraft(owner.Value, type.Value, request.Kind, channel.Value, settings.Value, recipients.Value, request.Mandatory);
    }

    public async Task<ErrorOr<Subscription>> GetOwnedAsync(Guid id, CancellationToken ct)
    {
        var subscription = await Queries.GetSubscriptionAsync(id, ct);
        return SignedInUser is not { } user
            || (subscription.IsError && subscription.Error.Type != Errors.NotFound)
            || (subscription.IsSuccess && subscription.Value.Owner == SubscriptionOwner.ForUser(user))
                ? subscription
                : _notStored;
    }

    public async Task<Error?> OwnerErrorAsync(Guid id, CancellationToken ct) =>
        SignedInUser is null ? null : (await GetOwnedAsync(id, ct)).Error;

    public string LocationOf(HttpRequest request, Guid id)
    {
        var path = $"{request.PathBase.Add(request.Path).ToUriComponent().TrimEnd('/')}/{id}";
        return QueryTenant is { } tenant ? path + QueryString.Create("tenantId", tenant.Value) : path;
    }

    private ErrorOr<ChannelSettings?> SettingsOf(SubscriptionRequest request, ChannelId? channel)
    {
        if (request.Settings is not { } json)
        {
            return (ChannelSettings?)null;
        }

        if (channel is not { } id)
        {
            return new Error(Errors.ValidationFailed, "Settings need a channel.");
        }

        var settings = SettingsJson.Read(Registry, id, json);
        return settings.IsError ? settings.Error : settings.Value;
    }
}
