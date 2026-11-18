using JasperFx.CodeGeneration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Wolverine;
using Wolverine.RabbitMQ;
using WolverineExample.Contracts;
using WolverineExample.Publisher;

Console.Title = "WolverineExample.Publisher";
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = args, ContentRootPath = AppContext.BaseDirectory });
var rabbitMq = RabbitMqOptions.Load(builder.Configuration);
builder.Services.AddWolverine(options => ConfigureWolverine(options, rabbitMq.Uri!));
using var host = builder.Build();
await host.StartAsync();

var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("WolverineExample.Publisher");
await using (var scope = host.Services.CreateAsyncScope())
{
    var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
    foreach (var domainEvent in SampleEvents(TimeProvider.System.GetUtcNow()))
    {
        await bus.SendAsync(domainEvent);
        logger.EventSent(domainEvent.GetType().Name, RabbitMqOptions.EventQueue);
    }
}

await host.StopAsync();

// The tenant "north" and its ids come from the seed of the consumer: Client 1 of Acme signs in, Globex is archived,
// Client 2 of Acme owns the project 7, and the maintenance concerns Acme and Globex.
static object[] SampleEvents(DateTimeOffset now) =>
[
    new UserLogin("north", UserId: 1, now),
    new CompanyArchived("north", CompanyId: 200, now),
    new ProjectCreated("north", ProjectId: 7, "Bridge", OwnerUserId: 2),
    new ProjectFinished("north", ProjectId: 7, "Bridge", OwnerUserId: 2),
    new TenantMaintenance("north", now.AddDays(1), now.AddDays(1).AddHours(2)),
];

// SendInline, the sync retry block, and the publisher confirms make SendAsync throw when the broker does not accept a
// message. AutoProvision declares the durable queue, so the broker keeps the events until the consumer starts.
static void ConfigureWolverine(WolverineOptions options, Uri broker)
{
    options.Discovery.DisableConventionalDiscovery();
    options.CodeGeneration.TypeLoadMode = TypeLoadMode.Static;
    options.Durability.UseSyncRetryBlock = true;
    options.UseRabbitMq(broker)
        .AutoProvision()
        .DisableSystemRequestReplyQueueDeclaration()
        .UseSenderConnectionOnly()
        .ConfigureChannelCreation(static channel =>
        {
            channel.PublisherConfirmationsEnabled = true;
            channel.PublisherConfirmationTrackingEnabled = true;
        });
    options.PublishAllMessages().ToRabbitQueue(RabbitMqOptions.EventQueue).SendInline();
}
