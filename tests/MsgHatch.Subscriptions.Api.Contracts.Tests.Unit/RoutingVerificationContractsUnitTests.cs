using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Persistence;
using MsgHatch.Subscriptions.Api.Contracts.Tests.Unit.TestTypes;
using MsgHatch.Subscriptions.Tests.Unit.TestTypes;
using MsgHatch.Subscriptions.Verification;
using NSubstitute;

namespace MsgHatch.Subscriptions.Api.Contracts.Tests.Unit;

/// <summary>The mappings of the routing verification bodies in both directions (D228).</summary>
public sealed class RoutingVerificationContractsUnitTests(ScenarioRegistry scenario) : IClassFixture<ScenarioRegistry>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void ToRequest_BodyOfARequest_GivesTheSameValues()
    {
        // Arrange
        var request = new RoutingVerificationRequest([new UserId("1"), new UserId("3")], ["en", "hu"], new MessageType("B"));

        // Act
        var roundTrip = RoutingVerificationBody.From(request).ToRequest().Value;

        // Assert
        Assert.Equal(
            ("1, 3", "en, hu", request.Type),
            (string.Join(", ", roundTrip.TestUsers.Select(static user => user.Value)), string.Join(", ", roundTrip.Languages), roundTrip.Type));
    }

    [Theory]
    [InlineData("", "B")]
    [InlineData("1", "1B")]
    public void ToRequest_ValueThatIsNotValid_GivesSerializationFailed(string user, string messageType)
    {
        // Act
        var request = new RoutingVerificationBody([user], ["en"], messageType).ToRequest();

        // Assert
        Assert.Equal(Errors.SerializationFailed.Code, request.Error?.Type.Code);
    }

    [Fact]
    public async Task ToReport_ResponseOfAReportOfTheScenario_GivesTheSameRowsAndTraces()
    {
        // Arrange
        var report = await AReportAsync();

        // Act
        var roundTrip = RoutingVerificationResponse.From(report, scenario.Registry).ToReport(scenario.Registry).Value;

        // Assert
        Assert.Equal(Describe(report, scenario.Registry), Describe(roundTrip, scenario.Registry));
    }

    [Fact]
    public async Task ToReport_RegistryWithNoTraceType_GivesTheRowsWithNoTrace()
    {
        // Arrange
        var response = RoutingVerificationResponse.From(await AReportAsync(), scenario.Registry);
        var registry = Substitute.For<IContentTypeRegistry>();
        registry.Entries.Returns([]);

        // Act
        var report = response.ToReport(registry).Value;

        // Assert
        Assert.Equal((response.Checks.Count, false), (report.Checks.Count, report.Checks.Any(static check => check.Trace is not null)));
    }

    [Theory]
    [InlineData("a target with no kind", null, "1", null)]
    [InlineData("an owner id with no level", null, null, "100")]
    public void ToCheck_ValueThatIsNotValid_GivesSerializationFailed(string row, string? targetKind, string? targetId, string? companyId)
    {
        // Arrange
        var check = new RoutingCheckResponse(
            "A", "basic", null, companyId, null, targetKind is null ? null : Enum.Parse<Domain.Routing.DeliveryTargetKind>(targetKind), targetId,
            "Email", "en", RoutingCheckOutcome.Passed, null, null, null);

        // Act
        var result = check.ToCheck(scenario.Registry);

        // Assert
        Assert.Equal((row, Errors.SerializationFailed.Code), (row, result.Error?.Type.Code));
    }

    private static string[] Describe(RoutingVerificationReport report, IContentTypeRegistry registry) =>
        [.. report.Checks.Select(check =>
            $"{check with { Trace = null }} {(check.Trace is null ? "no trace" : SettingsJson.Write(registry, check.Trace).GetRawText())}")];

    private async Task<RoutingVerificationReport> AReportAsync()
    {
        await using var scope = scenario.Services.CreateAsyncScope();
        await scope.ServiceProvider.SeedScenarioAsync();
        var report = await scope.ServiceProvider.GetRequiredService<IRoutingVerifier>()
            .VerifyAsync(new RoutingVerificationRequest([new UserId("1")], ["en", "hu"], new MessageType("B")), Ct);
        Assert.NotEmpty(report.Value.Checks);
        return report.Value;
    }
}
