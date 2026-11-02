using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Email.DependencyInjection;
using MsgHatch.Email.Engines;
using MsgHatch.Email.Tests.Unit.TestTypes;
using NSubstitute;

namespace MsgHatch.Email.Tests.Unit.Engines;

/// <summary>The standard logging wrapper tests for <see cref="IEmailTemplateEngine{TPayload}"/>.</summary>
public sealed class EmailTemplateEngineLoggingWrapperUnitTests
{
    private const string _address = "client1@example.com";
    private const string _subject = "Order 1001 shipped";
    private static readonly TenantKey _t1 = TenantKey.For(new TenantId("t1"));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task PrepareAsync_InnerTemplate_ReturnsItUnchanged()
    {
        // Arrange
        var template = Substitute.For<IEmailTemplate<OrderEmail>>();
        await using var provider = BuildWith(AnEngine(ErrorOr<IEmailTemplate<OrderEmail>>.ForValue(template)));
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
        await scope.ServiceProvider.GetRequiredService<IEmailTemplateEngine<OrderEmail>>()
            .PrepareAsync(new EmailTemplateRequest(new MessageType("TestEvent"), "en", Scope: null), Ct);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal(((string?)null, 2003), (record.GetStructuredStateValue("Tenant"), record.Id.Id));
    }

    [Fact]
    public async Task PrepareAsync_TenantServiceOfAKey_LogsTheTenantOfTheKey()
    {
        // Arrange
        await using var provider = BuildWith(AnEngine(ErrorOr<IEmailTemplate<OrderEmail>>.ForValue(Substitute.For<IEmailTemplate<OrderEmail>>())));
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
        var template = Substitute.For<IEmailTemplate<OrderEmail>>();
        template.RenderAsync(default!, default, default!, Ct)
            .ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<RenderedEmail>.ForValue(new RenderedEmail(_subject, "<p>html</p>", null))));
        await using var provider = BuildWith(AnEngine(ErrorOr<IEmailTemplate<OrderEmail>>.ForValue(template)));
        await using var scope = provider.CreateAsyncScope();
        var prepared = await PrepareAsync(scope.ServiceProvider);

        // Act
        await prepared.Value.RenderAsync(
            new OrderEmail(_address, 1, 1m, TestMessages.CreatedAtUtc), TestRoutes.AnEmailRoute(address: _address), CultureInfo.InvariantCulture, Ct);

        // Assert
        Assert.NotEmpty(provider.LogRecords());
        Assert.DoesNotContain(provider.LogRecords(), static record => record.ContainsText(_address) || record.ContainsText(_subject));
    }

    [Fact]
    public async Task AsEmail_EnableLoggingFalse_RegistersNoLoggingWrapper()
    {
        // Arrange
        var inner = AnEngine(new Error(Errors.NotFound));
        await using var provider = BuildWith(inner, enableLogging: false);
        await using var scope = provider.CreateAsyncScope();

        // Act
        var engine = scope.ServiceProvider.GetRequiredKeyedService<IEmailTemplateEngine<OrderEmail>>(_t1);

        // Assert
        Assert.Same(inner, engine);
    }

    private static IEmailTemplateEngine<OrderEmail> AnEngine(ErrorOr<IEmailTemplate<OrderEmail>> result)
    {
        var engine = Substitute.For<IEmailTemplateEngine<OrderEmail>>();
        engine.PrepareAsync(default, Ct).ReturnsForAnyArgs(ValueTask.FromResult(result));
        return engine;
    }

    private static ValueTask<ErrorOr<IEmailTemplate<OrderEmail>>> PrepareAsync(IServiceProvider scope) =>
        scope.GetRequiredKeyedService<IEmailTemplateEngine<OrderEmail>>(_t1)
            .PrepareAsync(new EmailTemplateRequest(new MessageType("TestEvent"), "en", Scope: null), Ct);

    private static ServiceProvider BuildWith(IEmailTemplateEngine<OrderEmail> inner, bool enableLogging = true)
    {
        var services = TestHost.CreateServices();
        services.AddMsgHatch(o => o.EnableLogging = enableLogging)
            .AddMessage("TestEvent", 1, TestJsonContext.Default.TestEvent, m => m.WithPayload<OrderEmail, TestOrderLoader>(p => p.AsEmail("OrderEmail", (_, _) => inner)));
        return services.Build();
    }
}
