using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Templates.DependencyInjection;
using MsgHatch.Templates;
using MsgHatch.Templates.Tests.Unit.TestTypes;
using NSubstitute;

namespace MsgHatch.Templates.Tests.Unit;

/// <summary>The standard logging wrapper tests for <see cref="ITemplateEngine{TPayload}"/>.</summary>
public sealed class TemplateEngineLoggingWrapperUnitTests
{
    private const string _address = "client1@example.com";
    private const string _subject = "Order 1001 shipped";
    private static readonly TenantKey _t1 = TenantKey.For(new TenantId("t1"));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task PrepareAsync_InnerTemplate_ReturnsItUnchanged()
    {
        // Arrange
        var template = Substitute.For<ITemplate<OrderEmail>>();
        await using var provider = BuildWith(AnEngine(ErrorOr<ITemplate<OrderEmail>>.ForValue(template)));
        await using var scope = provider.CreateAsyncScope();

        // Act
        var result = await PrepareAsync(scope.ServiceProvider);

        // Assert
        Assert.Same(template, result.Value);
    }

    [Fact]
    public async Task PrepareAsync_InnerError_ReturnsTheSameError()
    {
        // Arrange
        var error = new Error(Errors.NotFound);
        await using var provider = BuildWith(AnEngine(error));
        await using var scope = provider.CreateAsyncScope();

        // Act
        var result = await PrepareAsync(scope.ServiceProvider);

        // Assert
        Assert.Same(error, result.Error);
    }

    [Fact]
    public async Task PrepareAsync_TransientError_LogsAWarningWithItsEventId()
    {
        // Arrange
        await using var provider = BuildWith(AnEngine(new Error(Errors.Unavailable)));
        await using var scope = provider.CreateAsyncScope();

        // Act
        await PrepareAsync(scope.ServiceProvider);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal((LogLevel.Warning, 2003), (record.Level, record.Id.Id));
    }

    [Fact]
    public async Task PrepareAsync_PermanentError_LogsAnErrorWithItsEventId()
    {
        // Arrange
        await using var provider = BuildWith(AnEngine(new Error(Errors.NotFound)));
        await using var scope = provider.CreateAsyncScope();

        // Act
        await PrepareAsync(scope.ServiceProvider);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal((LogLevel.Error, 2003), (record.Level, record.Id.Id));
    }

    [Fact]
    public async Task PrepareAsync_NotFoundOfAnOptionalPart_LogsDebugWithItsEventId()
    {
        // Arrange
        await using var provider = BuildWith(AnEngine(new Error(Errors.NotFound)));
        await using var scope = provider.CreateAsyncScope();

        // Act
        await PrepareAsync(scope.ServiceProvider, optional: true);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal((LogLevel.Debug, 2003), (record.Level, record.Id.Id));
    }

    [Fact]
    public async Task PrepareAsync_TransientErrorOfAnOptionalPart_LogsAWarningWithItsEventId()
    {
        // Arrange
        await using var provider = BuildWith(AnEngine(new Error(Errors.Unavailable)));
        await using var scope = provider.CreateAsyncScope();

        // Act
        await PrepareAsync(scope.ServiceProvider, optional: true);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal((LogLevel.Warning, 2003), (record.Level, record.Id.Id));
    }

    [Fact]
    public async Task PrepareAsync_CancelledError_LogsInformationWithItsEventId()
    {
        // Arrange
        await using var provider = BuildWith(AnEngine(new Error(Errors.OperationCancelled)));
        await using var scope = provider.CreateAsyncScope();

        // Act
        await PrepareAsync(scope.ServiceProvider);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal((LogLevel.Information, 2003), (record.Level, record.Id.Id));
    }

    [Fact]
    public async Task PrepareAsync_ErrorOfTheEngineWithNoTenant_LogsNoTenant()
    {
        // Arrange
        await using var provider = BuildWith(AnEngine(new Error(Errors.NotFound)));
        await using var scope = provider.CreateAsyncScope();

        // Act
        await scope.ServiceProvider.GetRequiredService<ITemplateEngine<OrderEmail>>()
            .PrepareAsync(new TemplateRequest(new MessageType("TestEvent"), "en", Scope: null, "subject", TemplatePartKind.PlainText), Ct);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal(((string?)null, 2003), (record.GetStructuredStateValue("Tenant"), record.Id.Id));
    }

    [Fact]
    public async Task PrepareAsync_TenantServiceOfAKey_LogsTheTenantOfTheKey()
    {
        // Arrange
        await using var provider = BuildWith(AnEngine(ErrorOr<ITemplate<OrderEmail>>.ForValue(Substitute.For<ITemplate<OrderEmail>>())));
        await using var scope = provider.CreateAsyncScope();

        // Act
        await PrepareAsync(scope.ServiceProvider);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal(("t1", 2002), (record.GetStructuredStateValue("Tenant"), record.Id.Id));
    }

    [Fact]
    public async Task PrepareAsync_TemplateOfAnEmail_NoLogRecordContainsTheAddressOrTheSubject()
    {
        // Arrange
        var template = Substitute.For<ITemplate<OrderEmail>>();
        template.RenderAsync(default!, default!, Ct)
            .ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<string>.ForValue(_subject)));
        await using var provider = BuildWith(AnEngine(ErrorOr<ITemplate<OrderEmail>>.ForValue(template)));
        await using var scope = provider.CreateAsyncScope();
        var prepared = await PrepareAsync(scope.ServiceProvider);

        // Act
        await prepared.Value.RenderAsync(
            new OrderEmail(_address, 1, 1m, DateTime.UnixEpoch), CultureInfo.InvariantCulture, Ct);

        // Assert
        Assert.NotEmpty(provider.LogRecords());
        Assert.DoesNotContain(provider.LogRecords(), static record => record.ContainsText(_address) || record.ContainsText(_subject));
    }

    [Fact]
    public async Task AddTemplateEngine_EnableLoggingFalse_RegistersNoLoggingWrapper()
    {
        // Arrange
        var inner = AnEngine(new Error(Errors.NotFound));
        await using var provider = BuildWith(inner, enableLogging: false);
        await using var scope = provider.CreateAsyncScope();

        // Act
        var engine = scope.ServiceProvider.GetRequiredKeyedService<ITemplateEngine<OrderEmail>>(_t1);

        // Assert
        Assert.Same(inner, engine);
    }

    private static ITemplateEngine<OrderEmail> AnEngine(ErrorOr<ITemplate<OrderEmail>> result)
    {
        var engine = Substitute.For<ITemplateEngine<OrderEmail>>();
        engine.PrepareAsync(default, Ct).ReturnsForAnyArgs(ValueTask.FromResult(result));
        return engine;
    }

    private static ValueTask<ErrorOr<ITemplate<OrderEmail>>> PrepareAsync(IServiceProvider scope, bool optional = false) =>
        scope.GetRequiredKeyedService<ITemplateEngine<OrderEmail>>(_t1)
            .PrepareAsync(new TemplateRequest(new MessageType("TestEvent"), "en", Scope: null, "subject", TemplatePartKind.PlainText, optional), Ct);

    private static ServiceProvider BuildWith(ITemplateEngine<OrderEmail> inner, bool enableLogging = true)
    {
        var services = TestHost.CreateServices();
        services.AddMsgHatch(o => o.EnableLogging = enableLogging)
            .AddTemplateEngine<OrderEmail>("Email:OrderEmail", (_, _) => inner);
        return services.Build();
    }
}
