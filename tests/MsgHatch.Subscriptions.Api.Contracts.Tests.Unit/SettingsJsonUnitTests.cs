using System.Text.Json;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Email;
using MsgHatch.Subscriptions.Api.Contracts.Tests.Unit.TestTypes;

namespace MsgHatch.Subscriptions.Api.Contracts.Tests.Unit;

/// <summary>The settings of a body on the server (D229), and the HTTP concerns of the contracts package (D228).</summary>
public sealed class SettingsJsonUnitTests(ScenarioRegistry scenario) : IClassFixture<ScenarioRegistry>
{
    [Theory]
    [InlineData("Sms", """{"phone":"+3612345678"}""")]
    [InlineData("Email", """{"language":42}""")]
    public void Read_ChannelWithNoSettingsTypeOrJsonThatTheTypeCannotRead_GivesValidationFailed(string channel, string json)
    {
        // Arrange
        using var document = JsonDocument.Parse(json);

        // Act
        var settings = SettingsJson.Read(scenario.Registry, new ChannelId(channel), document.RootElement);

        // Assert
        Assert.Equal((channel, Errors.ValidationFailed.Code), (channel, settings.Error?.Type.Code));
    }

    [Fact]
    public void Read_SettingsOfARegisteredType_GivesTheTypedSettings()
    {
        // Arrange
        using var document = JsonDocument.Parse("""{"language":"hu","format":"Html"}""");

        // Act
        var settings = SettingsJson.Read(scenario.Registry, EmailChannel.Id, document.RootElement);

        // Assert
        Assert.Equal(new EmailChannelSettings("hu", EmailFormat.Html), settings.Value);
    }

    [Fact]
    public void Write_ValueOfNoRegisteredType_ThrowsInvalidOperationException()
    {
        // Act
        var exception = Record.Exception(() => SettingsJson.Write(scenario.Registry, new object()));

        // Assert
        Assert.IsType<InvalidOperationException>(exception);
    }

    [Fact]
    public void ContractsAssembly_ReferencesAndPublicTypes_HaveNoHttpConcernAndNoException()
    {
        // Arrange
        var assembly = typeof(SubscriptionRequest).Assembly;

        // Act
        string[] httpReferences =
        [
            .. assembly.GetReferencedAssemblies().Select(static reference => reference.Name!)
                .Where(static name => name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal) || name == "System.Net.Http"),
        ];
        string[] exceptions = [.. assembly.GetExportedTypes().Where(static type => typeof(Exception).IsAssignableFrom(type)).Select(static type => type.Name)];

        // Assert
        Assert.Equal((0, 0, true), (httpReferences.Length, exceptions.Length, assembly.GetExportedTypes().Length > 0));
    }
}
