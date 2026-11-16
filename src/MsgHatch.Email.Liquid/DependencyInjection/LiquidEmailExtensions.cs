using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Email.DependencyInjection;

namespace MsgHatch.Email.Liquid.DependencyInjection;

/// <summary>The setup method of the Liquid engine (section 4.7, D205, D206).</summary>
public static class LiquidEmailExtensions
{
    /// <summary>
    /// Registers the message type on the email channel for one payload type, with the Liquid engine (D206). The engine
    /// renders the parts <c>subject</c>, <c>html</c>, and <c>text</c> of the template store, with the payload as the
    /// model (D209), so the payload type needs no <c>[PlaceholderPayload]</c>. All Liquid registrations of one payload
    /// type share one engine. With the role <see cref="MsgHatchRoles.Consumer"/>, the startup validation needs a
    /// template store.
    /// </summary>
    /// <typeparam name="TEvent">The type of the domain event.</typeparam>
    /// <typeparam name="TPayload">The payload type.</typeparam>
    /// <param name="payload">The builder of the payload type.</param>
    /// <param name="name">The name of the registration.</param>
    /// <param name="configure">The configuration of the retry options of this registration.</param>
    /// <returns>The builder of the payload type.</returns>
    public static PayloadBuilder<TEvent, TPayload> AsLiquidEmail<TEvent, TPayload>(
        this PayloadBuilder<TEvent, TPayload> payload, string name, Action<ChannelBuilder<EmailMessage>>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return payload.AsEmail(name, LiquidTemplates.Engine, configure);
    }
}
