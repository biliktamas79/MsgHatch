using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MsgHatch.AdminUI.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Management;
using MsgHatch.Domain.Routing;
using NSubstitute;

namespace MsgHatch.AdminUI.Tests.Unit.TestTypes;

/// <summary>
/// The bUnit context of the registry pages: <see cref="IRegistryQueries"/> of <see cref="TenantKey.None"/> is a substitute
/// that gives the channels Email, Sms, and Fax, and the message types Requests on Email and Sms, Notices on Email (disabled),
/// and Invoices on Email (disabled on Email).
/// </summary>
public abstract class RegistryPageContext : BunitContext
{
    public static readonly ChannelId Email = new("Email");
    public static readonly ChannelId Sms = new("Sms");
    public static readonly ChannelId Fax = new("Fax");

    protected RegistryPageContext()
    {
        Queries.GetChannelsAsync(Arg.Any<CancellationToken>()).Returns(Ok(Channels));
        Queries.GetMessageTypesAsync(Arg.Any<CancellationToken>()).Returns(Ok(MessageTypes));
        Services.AddKeyedSingleton(TenantKey.None, Queries);
        Services.AddMsgHatchAdminUI();
    }

    public static IReadOnlyList<RegistryChannel> Channels { get; } =
    [
        new(Email, "Email channel", "Sends emails", ChannelFeatures.Languages | ChannelFeatures.RichText, Enabled: true, Revision: 2),
        new(Sms, "Text messages", null, ChannelFeatures.None, Enabled: false, Revision: 0),
        new(Fax, "Fax", null, ChannelFeatures.GroupDelivery, Enabled: true, Revision: 0),
    ];

    public static IReadOnlyList<RegistryMessageType> MessageTypes { get; } =
    [
        new(new MessageType("Requests"), "Requests of a client", null, [Email, Sms], Enabled: true, [], Revision: 0),
        new(new MessageType("Notices"), null, null, [Email], Enabled: false, [], Revision: 1),
        new(new MessageType("Invoices"), "Invoices", null, [Email], Enabled: true, [Email], Revision: 3),
    ];

    protected IRegistryQueries Queries { get; } = Substitute.For<IRegistryQueries>();

    protected static ValueTask<ErrorOr<T>> Ok<T>(T value) => ValueTask.FromResult(ErrorOr<T>.ForValue(value));

    protected static ValueTask<ErrorOr<T>> Fail<T>(ErrorType type) => ValueTask.FromResult(ErrorOr<T>.ForError(new Error(type)));
}
