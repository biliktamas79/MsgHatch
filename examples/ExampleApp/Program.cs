using ExampleApp;
using ExampleApp.Startup;

Console.Title = "ExampleApp";
var app = ExampleApplication.Build(WebApplication.CreateBuilder(args));
await ExampleStartup.RunAsync(app.Services, CancellationToken.None);
if (app.Environment.IsDevelopment())
{
    await ExampleStartup.VerifyRoutingAsync(app.Services, CancellationToken.None);
}

await app.RunAsync();
