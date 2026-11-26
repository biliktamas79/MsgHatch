using System.Text.Json;
using MsgHatch.Channels.Email;
using MsgHatch.Channels.SignalR;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Subscriptions.Api.Contracts.Tests.Unit.TestTypes;
using MsgHatch.Subscriptions.Tests.Unit.TestTypes;

namespace MsgHatch.Subscriptions.Api.Contracts.Tests.Unit;

/// <summary>The mappings of a subscription request and its recipients in both directions (D228, D229).</summary>
public sealed class SubscriptionRequestUnitTests(ScenarioRegistry scenario) : IClassFixture<ScenarioRegistry>
{
    public static TheoryData<string, SubscriptionDraft> DraftRows => new()
    {
        { "a user subscription with typed settings", new SubscriptionDraft(SubscriptionOwner.ForUser(new UserId("1")), new MessageType("B"), SubscriptionKind.Channel, EmailChannel.Id, new EmailChannelSettings("hu", EmailFormat.Html)) },
        { "a default opt-out for all channels", new SubscriptionDraft(SubscriptionOwner.Default, new MessageType("A"), SubscriptionKind.OptOut, null, null) },
        {
            "a mandatory company subscription for named users",
            new SubscriptionDraft(SubscriptionOwner.ForCompany(Scenario.Acme), new MessageType("D"), SubscriptionKind.Channel, EmailChannel.Id, new EmailChannelSettings(), SubscriptionRecipients.ForUsers([new UserId("2"), new UserId("3")]), Mandatory: true)
        },
        { "a company subscription for the company", new SubscriptionDraft(SubscriptionOwner.ForCompany(Scenario.Acme), new MessageType("B"), SubscriptionKind.Channel, EmailChannel.Id, new EmailChannelSettings(), SubscriptionRecipients.Company) },
    };

    [Theory]
    [MemberData(nameof(DraftRows))]
    public void ToDraft_RequestOfADraft_GivesAnEqualDraft(string row, SubscriptionDraft draft)
    {
        // Act
        var roundTrip = SubscriptionRequest.From(draft, scenario.Registry, revision: 3).ToDraft(scenario.Registry);

        // Assert
        Assert.Equal((row, draft), (row, roundTrip.Value));
    }

    [Fact]
    public void FromAndToDraft_SettingsOfTheTypeOfAnotherChannel_NameTheirSettingsTypeAndGiveAnEqualDraft_D247()
    {
        // Arrange
        var draft = new SubscriptionDraft(
            SubscriptionOwner.ForUser(new UserId("1")), new MessageType("C"), SubscriptionKind.Channel, EmailChannel.Id, new SignalRChannelSettings("hu"));

        // Act
        var request = SubscriptionRequest.From(draft, scenario.Registry);
        var roundTrip = request.ToDraft(scenario.Registry);

        // Assert
        Assert.Equal(("Settings:SignalR", draft), (request.SettingsType, roundTrip.Value));
    }

    [Fact]
    public void FromAndToDraft_UnknownSettingsTypeOrNoSettings_NameNoSettingsTypeAndReadTheSettingsOfTheChannel_D247()
    {
        // Arrange
        using var document = JsonDocument.Parse("""{"language":"hu"}""");
        var request = new SubscriptionRequest(
            "C", SubscriptionKind.Channel, SubscriptionLevel.User, UserId: "1", Channel: "Email", Settings: document.RootElement, SettingsType: "Settings:Fax");
        var optOut = new SubscriptionDraft(SubscriptionOwner.Default, new MessageType("A"), SubscriptionKind.OptOut, null, null);

        // Act
        var draft = request.ToDraft(scenario.Registry);

        // Assert
        Assert.Equal((new EmailChannelSettings("hu"), null), (draft.Value.Settings, SubscriptionRequest.From(optOut, scenario.Registry).SettingsType));
    }

    [Fact]
    public void From_JsonChannelSettings_WritesTheJsonWithNoChange()
    {
        // Arrange
        const string property = "phone";
        const string phone = "+3612345678";
        using var document = JsonDocument.Parse($$"""{"{{property}}":"{{phone}}"}""");
        var draft = new SubscriptionDraft(
            SubscriptionOwner.ForUser(new UserId("1")), new MessageType("A"), SubscriptionKind.Channel, new ChannelId("Sms"), new JsonChannelSettings(document.RootElement));

        // Act
        var request = SubscriptionRequest.From(draft, scenario.Registry);
        var json = JsonSerializer.Serialize(request, SubscriptionEndpointJson.Default.SubscriptionRequest);

        // Assert
        using var written = JsonDocument.Parse(json);
        Assert.Equal(
            (phone, null),
            (written.RootElement.GetProperty("settings").GetProperty(property).GetString(), request.SettingsType));
    }

    [Theory]
    [InlineData("no level", null, null, null)]
    [InlineData("a default owner with a company", "Default", "100", null)]
    [InlineData("a user owner with an empty user id", "User", null, "")]
    public void ToDraft_OwnerThatIsNotValid_GivesSerializationFailed(string row, string? level, string? companyId, string? userId)
    {
        // Arrange
        var request = new SubscriptionRequest(
            "A", SubscriptionKind.OptOut, level is null ? null : Enum.Parse<SubscriptionLevel>(level), companyId, userId);

        // Act
        var draft = request.ToDraft(scenario.Registry);

        // Assert
        Assert.Equal((row, Errors.SerializationFailed.Code), (row, draft.Error?.Type.Code));
    }

    [Fact]
    public void ToRecipients_UnknownKind_GivesSerializationFailed()
    {
        // Act
        var recipients = new SubscriptionRecipientsBody((RecipientKind)9).ToRecipients();

        // Assert
        Assert.Equal(Errors.SerializationFailed.Code, recipients.Error?.Type.Code);
    }
}
