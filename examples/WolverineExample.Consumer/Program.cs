using WolverineExample.Consumer;
using WolverineExample.Consumer.Startup;

var app = ConsumerApplication.Build(WebApplication.CreateBuilder(args));
await ConsumerStartup.SeedAsync(app.Services, CancellationToken.None);
await app.RunAsync();
