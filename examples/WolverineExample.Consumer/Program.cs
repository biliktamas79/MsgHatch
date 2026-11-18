using WolverineExample.Consumer;
using WolverineExample.Consumer.Startup;

Console.Title = "WolverineExample.Consumer";
var app = ConsumerApplication.Build(WebApplication.CreateBuilder(args));
await ConsumerStartup.SeedAsync(app.Services, CancellationToken.None);
await app.RunAsync();
