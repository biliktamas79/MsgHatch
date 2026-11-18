using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using MsgHatch.AdminUI.DependencyInjection;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Management;
using MsgHatch.Domain.Routing;
using NSubstitute;

namespace MsgHatch.AdminUI.Tests.Unit.TestTypes;

/// <summary>
/// The bUnit context of the registry pages: <see cref="IRegistryQueries"/> of <see cref="TenantKey.None"/> is a substitute
/// that gives the channels Email, Sms, and Fax, and the message types Requests on Email and Sms, Notices on Email (disabled),
/// and Invoices on Email (disabled on Email). <see cref="IRegistryCommands"/> of <see cref="TenantKey.None"/> is a
/// substitute too.
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
        Queries.GetChannelHistoryAsync(default, Arg.Any<CancellationToken>()).ReturnsForAnyArgs(Ok<IReadOnlyList<ChannelState>>([]));
        Services.AddKeyedSingleton(TenantKey.None, Queries);
        Services.AddKeyedSingleton(TenantKey.None, Commands);
        Services.AddSingleton<TimeProvider>(Time);
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

    protected IRegistryCommands Commands { get; } = Substitute.For<IRegistryCommands>();

    /// <summary>The clock of the pages, at 2030-01-01T00:00:00Z.</summary>
    protected FakeTimeProvider Time { get; } = new(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero));

    protected DateTime Now => Time.GetUtcNow().UtcDateTime;

    /// <summary>The text of an element with each run of white space as one space.</summary>
    protected static string SpacedTextOf(AngleSharp.Dom.IElement element) =>
        string.Join(' ', element.TextContent.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    protected static ValueTask<ErrorOr<T>> Ok<T>(T value) => ValueTask.FromResult(ErrorOr<T>.ForValue(value));

    protected static ValueTask<ErrorOr<T>> Fail<T>(ErrorType type) => ValueTask.FromResult(ErrorOr<T>.ForError(new Error(type)));
}
