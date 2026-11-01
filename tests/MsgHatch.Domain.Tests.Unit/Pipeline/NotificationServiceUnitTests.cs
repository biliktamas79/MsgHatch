using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Pipeline;
using MsgHatch.Domain.Tests.Unit.TestTypes;

namespace MsgHatch.Domain.Tests.Unit.Pipeline;

/// <summary>The pipeline tests of <see cref="INotificationService"/>, with substitutes through the factory overloads.</summary>
public sealed partial class NotificationServiceUnitTests
{
    private static readonly TenantId _tenant = new("t1");
    private static readonly TenantKey _t1 = TenantKey.For(_tenant);

    private readonly FakeTimeProvider _time = TestHost.NewFakeTime();
    private readonly PipelineDoubles _doubles = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static MessageContext AContext(TenantId? tenant = null, MessageFlags flags = MessageFlags.None, Guid? id = null) =>
        new(id ?? Guid.NewGuid(), tenant, flags);

    private static async Task<ErrorOr<NotificationResult>> ProcessAsync<TEvent>(
        IServiceProvider provider, TEvent domainEvent, MessageContext context, CancellationToken? ct = null)
    {
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<INotificationService>().ProcessAsync(domainEvent, context, ct ?? Ct);
    }

    private static Task<ErrorOr<NotificationResult>> ProcessAsync(
        IServiceProvider provider, MessageContext context, CancellationToken? ct = null) =>
        ProcessAsync(provider, new TestEvent("text"), context, ct);
}
