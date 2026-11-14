using System.Buffers.Binary;
using System.Security.Cryptography;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Pipeline;
using Wolverine;
using WolverineExample.Consumer.Channels;
using WolverineExample.Consumer.Notifications;
using WolverineExample.Consumer.Recipients;
using WolverineExample.Contracts;

namespace WolverineExample.Consumer;

/// <summary>
/// The Wolverine handler of the domain events of the RabbitMQ queue. It gives each event to the MsgHatch inbox in a DI
/// scope of its own (D130), with the tenant of the event and the id of the Wolverine envelope as the message id (D20,
/// D115). So a second delivery of the same envelope stores nothing new. A failed accept throws, so Wolverine retries
/// the envelope.
/// </summary>
/// <param name="scopes">The factory of the DI scope of one envelope.</param>
/// <param name="logger">The logger.</param>
public sealed class DomainEventReceiver(IServiceScopeFactory scopes, ILogger<DomainEventReceiver> logger)
{
    /// <summary>Accepts a sign-in.</summary>
    /// <param name="domainEvent">The event.</param>
    /// <param name="envelope">The Wolverine envelope of the event.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task that completes when the inbox has the event.</returns>
    public Task Handle(UserLogin domainEvent, Envelope envelope, CancellationToken ct) => AcceptAsync(domainEvent, static e => e.TenantId, envelope, ct);

    /// <summary>Accepts an archive of a company.</summary>
    /// <param name="domainEvent">The event.</param>
    /// <param name="envelope">The Wolverine envelope of the event.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task that completes when the inbox has the event.</returns>
    public Task Handle(CompanyArchived domainEvent, Envelope envelope, CancellationToken ct) => AcceptAsync(domainEvent, static e => e.TenantId, envelope, ct);

    /// <summary>Accepts a new project.</summary>
    /// <param name="domainEvent">The event.</param>
    /// <param name="envelope">The Wolverine envelope of the event.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task that completes when the inbox has the event.</returns>
    public Task Handle(ProjectCreated domainEvent, Envelope envelope, CancellationToken ct) => AcceptAsync(domainEvent, static e => e.TenantId, envelope, ct);

    /// <summary>Accepts a finished project.</summary>
    /// <param name="domainEvent">The event.</param>
    /// <param name="envelope">The Wolverine envelope of the event.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task that completes when the inbox has the event.</returns>
    public Task Handle(ProjectFinished domainEvent, Envelope envelope, CancellationToken ct) => AcceptAsync(domainEvent, static e => e.TenantId, envelope, ct);

    /// <summary>Accepts one <see cref="CompanyMaintenance"/> for each company of the tenant.</summary>
    /// <param name="domainEvent">The event.</param>
    /// <param name="envelope">The Wolverine envelope of the event.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task that completes when the inbox has the event of each company.</returns>
    public async Task Handle(TenantMaintenance domainEvent, Envelope envelope, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        ArgumentNullException.ThrowIfNull(envelope);
        var tenant = TenantOf(domainEvent.TenantId);
        await using var scope = scopes.CreateAsyncScope();
        var companies = await ConsumerDirectory.For(scope.ServiceProvider, TenantKey.For(tenant)).GetCompanyIdsAsync(ct);
        foreach (var company in companies.IsError ? throw Failed(companies.Error) : companies.Value)
        {
            var context = new MessageContext(CompanyMessageId(envelope.Id, company), tenant);
            await AcceptAsync(scope.ServiceProvider, new CompanyMaintenance(company, domainEvent), context, ct);
        }
    }

    private async Task AcceptAsync<TEvent>(TEvent domainEvent, Func<TEvent, string> tenantOf, Envelope envelope, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        ArgumentNullException.ThrowIfNull(envelope);
        var context = new MessageContext(envelope.Id, TenantOf(tenantOf(domainEvent)));
        await using var scope = scopes.CreateAsyncScope();
        await AcceptAsync(scope.ServiceProvider, domainEvent, context, ct);
    }

    private async Task AcceptAsync<TEvent>(IServiceProvider services, TEvent domainEvent, MessageContext context, CancellationToken ct)
    {
        var accepted = await services.GetRequiredService<INotificationService>().AcceptAsync(domainEvent, context, ct);
        if (accepted.IsError)
        {
            throw Failed(accepted.Error);
        }

        logger.EventAccepted(typeof(TEvent).Name, context.MessageId, context.Tenant?.Value);
    }

    /// <summary>Gives the same id for the same envelope and company, so a second delivery of the envelope stores nothing new.</summary>
    private static Guid CompanyMessageId(Guid envelopeId, int company)
    {
        Span<byte> input = stackalloc byte[20];
        envelopeId.TryWriteBytes(input);
        BinaryPrimitives.WriteInt32BigEndian(input[16..], company);
        return new Guid(SHA256.HashData(input).AsSpan(0, 16));
    }

    private static TenantId TenantOf(string tenant)
    {
        var id = TenantId.Create(tenant);
        return id.IsError ? throw Failed(id.Error) : id.Value;
    }

    private static InvalidOperationException Failed(Error error) =>
        new($"The consumer could not accept the event: {error.Type.Name}: {error.Message}");
}
