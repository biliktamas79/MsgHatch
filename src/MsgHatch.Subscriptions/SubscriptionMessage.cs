using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Domain.Identity;

namespace MsgHatch.Subscriptions;

/// <summary>
/// The subscription data of one message type: its scope function from <c>WithScope</c> (D16), and its test cases from
/// <c>WithTestEvent</c> (section 4.18). The subscription router needs the scope function, so a message type with the
/// subscription router and no scope function fails the startup validation.
/// </summary>
/// <typeparam name="TEvent">The type of the domain event.</typeparam>
internal sealed class SubscriptionMessage<TEvent> : ISubscriptionMessage
{
    public Func<TEvent, NotificationScope>? Scope { get; set; }

    public List<(string Name, Func<UserId, TEvent> Create)> TestCases { get; } = [];

    public Type EventType => typeof(TEvent);

    public bool HasScope => Scope is not null;

    /// <summary>Gives the subscription data of the message type, and registers it at the first call.</summary>
    public static SubscriptionMessage<TEvent> Of(IServiceCollection services) =>
        services.FirstOrDefault(static descriptor => descriptor.ServiceType == typeof(SubscriptionMessage<TEvent>))?.ImplementationInstance
            as SubscriptionMessage<TEvent>
        ?? Add(services);

    private static SubscriptionMessage<TEvent> Add(IServiceCollection services)
    {
        var message = new SubscriptionMessage<TEvent>();
        services.AddSingleton(message);
        services.AddSingleton<ISubscriptionMessage>(message);
        return message;
    }
}
