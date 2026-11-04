using System.Text.Json;
using System.Text.Json.Serialization;
using MsgHatch.Domain.Identity;

namespace MsgHatch.Subscriptions.Resolution;

/// <summary>
/// Writes a <see cref="SubscriptionOwner"/> as <c>{ "level": "User", "company": null, "user": "1" }</c> (section 4.5), and
/// reads it back through its factories, because the owner has no public constructor. An owner whose ids do not match
/// its level is not valid JSON.
/// </summary>
internal sealed class SubscriptionOwnerJsonConverter : JsonConverter<SubscriptionOwner>
{
    public override SubscriptionOwner Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("A subscription owner is a JSON object.");
        }

        string? level = null, company = null, user = null;
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            var name = reader.GetString();
            reader.Read();
            switch (name)
            {
                case "level":
                    level = reader.GetString();
                    break;
                case "company":
                    company = reader.GetString();
                    break;
                case "user":
                    user = reader.GetString();
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }

        return (level, company, user) switch
        {
            (nameof(SubscriptionLevel.Default), null, null) => SubscriptionOwner.Default,
            (nameof(SubscriptionLevel.Company), not null, null) => SubscriptionOwner.ForCompany(new CompanyId(company)),
            (nameof(SubscriptionLevel.User), null, not null) => SubscriptionOwner.ForUser(new UserId(user)),
            _ => throw new JsonException("A subscription owner has the level Default, Company, or User, and only the id of its level."),
        };
    }

    public override void Write(Utf8JsonWriter writer, SubscriptionOwner value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("level", value.Level.ToString());
        writer.WriteString("company", value.Company?.Value);
        writer.WriteString("user", value.User?.Value);
        writer.WriteEndObject();
    }
}
