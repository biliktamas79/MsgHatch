using System.Text.Json;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Subscriptions.Api.Contracts.Tests.Unit.TestTypes;
using MsgHatch.Subscriptions.Tests.Unit.TestTypes;

namespace MsgHatch.Subscriptions.Api.Contracts.Tests.Unit;

/// <summary>The mappings of a subscription response in both directions (D228, D229).</summary>
public sealed class SubscriptionResponseUnitTests(ScenarioRegistry scenario) : IClassFixture<ScenarioRegistry>
{
    public static TheoryData<string> ScenarioRows => [.. Scenario.Subscriptions.Select(static subscription => Scenario.Label(subscription.Id))];

    public static TheoryData<string, string> InvalidResponseRows => new()
    {
        { "an empty user id", """{"level":"User","userId":""}""" },
        { "a company id with a control character", """{"level":"Company","companyId":"acme\u0001"}""" },
        { "a user id of a company owner", """{"level":"Company","companyId":"100","userId":"1"}""" },
        { "a message type that is not a name", """{"messageType":"1A"}""" },
        { "a channel that is not a name", """{"channel":"E-mail"}""" },
        { "an unknown kind", """{"kind":7}""" },
        { "settings that the settings type cannot read", """{"settings":{"language":42}}""" },
        { "settings with no channel", """{"channel":null,"kind":"OptOut","settings":{"language":"hu"}}""" },
        { "named users with an empty user", """{"recipients":{"kind":"NamedUsers","users":[""]}}""" },
        { "users of the scope users", """{"recipients":{"kind":"ScopeUsers","users":["1"]}}""" },
    };

    [Theory]
    [MemberData(nameof(ScenarioRows))]
    public void ToSubscription_ResponseOfAScenarioSubscription_GivesAnEqualSubscriptionWithTypedSettings(string label)
    {
        // Arrange
        var subscription = Scenario.Subscriptions.Single(subscription => Scenario.Label(subscription.Id) == label);

        // Act
        var roundTrip = SubscriptionResponse.From(subscription, scenario.Registry).ToSubscription(scenario.Registry);

        // Assert
        Assert.Equal((label, subscription), (label, roundTrip.Value));
    }

    [Fact]
    public void ToSubscription_SettingsOfAChannelWithNoSettingsType_GivesJsonChannelSettingsWithTheSameJson()
    {
        // Arrange
        using var document = JsonDocument.Parse("""{"phone":"+3612345678","priority":2}""");
        var subscription = Scenario.S6 with { Channel = new ChannelId("Sms"), Settings = new JsonChannelSettings(document.RootElement.Clone()) };

        // Act
        var response = SubscriptionResponse.From(subscription, scenario.Registry);
        var roundTrip = response.ToSubscription(scenario.Registry);

        // Assert
        var settings = Assert.IsType<JsonChannelSettings>(roundTrip.Value.Settings);
        Assert.Equal(
            ("""{"phone":"+3612345678","priority":2}""", subscription with { Settings = null }),
            (settings.Json.GetRawText(), roundTrip.Value with { Settings = null }));
    }

    [Theory]
    [MemberData(nameof(InvalidResponseRows))]
    public void ToSubscription_ResponseWithAValueThatIsNotValid_GivesSerializationFailed(string row, string change)
    {
        // Arrange
        var response = ResponseWith(change);

        // Act
        var subscription = response.ToSubscription(scenario.Registry);

        // Assert
        Assert.Equal((row, Errors.SerializationFailed.Code), (row, subscription.Error?.Type.Code));
    }

    private SubscriptionResponse ResponseWith(string change)
    {
        var json = JsonSerializer.SerializeToNode(SubscriptionResponse.From(Scenario.S6, scenario.Registry), SubscriptionEndpointJson.Default.SubscriptionResponse)!.AsObject();
        foreach (var property in JsonDocument.Parse(change).RootElement.EnumerateObject())
        {
            json[property.Name] = System.Text.Json.Nodes.JsonNode.Parse(property.Value.GetRawText());
        }

        return json.Deserialize(SubscriptionEndpointJson.Default.SubscriptionResponse)!;
    }
}
