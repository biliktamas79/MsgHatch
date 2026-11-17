using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Broker;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Outbox;
using MsgHatch.Domain.Pipeline;
using MsgHatch.Domain.Routing;
using MsgHatch.Email;
using MsgHatch.SignalR;
using MsgHatch.Subscriptions.Resolution;
using MsgHatch.Subscriptions.Tests.Unit.TestTypes;

namespace MsgHatch.Subscriptions.Tests.Unit;

/// <summary>
/// The pipeline examples P1 to P7 of <c>docs/subscription-domain.md</c>, end to end on the in-memory outbox. Each example
/// asserts the whole list of outgoing messages: the target, the channel, the address and the subject of an email, which
/// tell the language, the body kind, and the applied subscription and language source of the resolution trace.
/// </summary>
public sealed partial class SubscriptionRouterUnitTests
{
    [Fact]
    public async Task ProcessAsync_AToUsers1And2_StoresAnEmailInTheLanguageOfEachUser_P1()
    {
        // Arrange
        await using var provider = BuildScenarioHost();

        // Act
        var result = await provider.ProcessAsync(new EventA(["1", "2"]));

        // Assert
        Assert.Equal(
            ("2 stored, 0 failed",
                "u:1 / Email / client1@acme.example / A in English / html / #1 UserUiLanguage; "
                + "u:2 / Email / client2@acme.example / A magyarul / html / #1 UserUiLanguage"),
            (ResultText(result.Value), OutboxText(provider)));
    }

    [Fact]
    public async Task ProcessAsync_BToUsers1And2_StoresATextEmailToUser1AndAnHtmlEmailToTheCompany_P2()
    {
        // Arrange
        await using var provider = BuildScenarioHost();

        // Act
        var result = await provider.ProcessAsync(new EventB(["1", "2"]));

        // Assert
        Assert.Equal(
            ("2 stored, 0 failed",
                "c:100 / Email / admin@acme.example / B magyarul / html / #10 CompanyDefaultLanguage; "
                + "u:1 / Email / client1@acme.example / B in English / text / #6 ChannelSettings"),
            (ResultText(result.Value), OutboxText(provider)));
    }

    [Fact]
    public async Task ProcessAsync_CToTheCompanyAcme_StoresOneSignalRMessageToTheCompanyWithTheMethodC_P3()
    {
        // Arrange
        await using var provider = BuildScenarioHost();

        // Act
        var result = await provider.ProcessAsync(new EventC(Company: "100"));

        // Assert
        Assert.Equal(("1 stored, 0 failed", "c:100 / SignalR / C / #3 CompanyDefaultLanguage"), (ResultText(result.Value), OutboxText(provider)));
    }

    [Fact]
    public async Task ProcessAsync_DToTheCompanyGlobex_StoresASignalRMessageToTheCompanyAndAnEmailToUser3_P4()
    {
        // Arrange
        await using var provider = BuildScenarioHost();

        // Act
        var result = await provider.ProcessAsync(new EventD(Company: "200"));

        // Assert
        Assert.Equal(
            ("2 stored, 0 failed",
                "c:200 / SignalR / D / #8 CompanyDefaultLanguage; u:3 / Email / client3@globex.example / D in English / html / #9 ChannelSettings"),
            (ResultText(result.Value), OutboxText(provider)));
    }

    [Fact]
    public async Task ProcessAsync_AToUsers1And5_StoresTheEmailOfUser1AndFailsUser5WithNoAddress_P5()
    {
        // Arrange
        await using var provider = BuildScenarioHost();

        // Act
        var result = await provider.ProcessAsync(new EventA(["1", "5"]));

        // Assert
        Assert.Equal(
            ("1 stored, 1 failed: 400", "u:1 / Email / client1@acme.example / A in English / html / #1 UserUiLanguage"),
            (ResultText(result.Value), OutboxText(provider)));
    }

    [Fact]
    public async Task ProcessAsync_SameMessageIdTwoTimes_GivesTheSameOutgoingMessageId_P6()
    {
        // Arrange
        await using var provider = BuildScenarioHost();
        var messageId = Guid.NewGuid();
        var first = await provider.ProcessAsync(new EventA(["1"]), messageId: messageId);

        // Act
        var second = await provider.ProcessAsync(new EventA(["1"]), messageId: messageId);

        // Assert
        Assert.Equal(
            ("1 stored, 0 failed", "1 stored, 0 failed", 1),
            (ResultText(first.Value), ResultText(second.Value), provider.GetRequiredService<InMemoryOutboxStore>().GetMessages().Count));
    }

    [Fact]
    public async Task ProcessAsync_DToUser1_StoresTheEmailOfTheNamedUser2AfterASecondDirectoryCall_P7()
    {
        // Arrange
        await using var provider = BuildScenarioHost();

        // Act
        var result = await provider.ProcessAsync(new EventD(["1"]));

        // Assert
        Assert.Equal(
            ("1 stored, 0 failed", "u:2 / Email / client2@acme.example / D magyarul / html / #11 UserUiLanguage", "GetUsersAsync(u:1) GetUsersAsync(u:2)"),
            (ResultText(result.Value), OutboxText(provider), string.Join(" ", ScenarioDoubles.Calls(_directories[TenantKey.None]))));
    }

    private static string ResultText(NotificationResult result) =>
        $"{result.Stored} stored, {result.Failed} failed"
        + (result.Errors.Count == 0 ? "" : $": {string.Join(",", result.Errors.Select(static error => error.Type.Code))}");

    private static string OutboxText(IServiceProvider provider) =>
        string.Join("; ", provider.GetRequiredService<InMemoryOutboxStore>().GetMessages().Select(Describe).Order(StringComparer.Ordinal));

    private static string Describe(OutgoingMessage message)
    {
        var target = $"{(message.Target.Kind == DeliveryTargetKind.User ? "u" : "c")}:{message.Target.Id}";
        var trace = (ResolutionTrace)message.Trace!;
        var source = $"{Scenario.Label(trace.Applied.Id)} {trace.Language}";
        return message.Content switch
        {
            EmailMessage email => $"{target} / {message.Channel.Name} / {email.To} / {email.Subject} / {BodyKind(email)} / {source}",
            SignalREnvelope<Notice> or BrokerEnvelope<Notice> => $"{target} / {message.Channel.Name} / {message.Type.Name} / {source}",
            _ => $"{target} / {message.Channel.Name} / unknown content",
        };
    }

    private static string BodyKind(EmailMessage email) => (email.HtmlBody, email.TextBody) switch
    {
        (not null, null) => "html",
        (null, not null) => "text",
        _ => "html and text",
    };
}
