using ExampleApp;

var app = ExampleApplication.Build(WebApplication.CreateBuilder(args));
await ExampleStartup.RunAsync(app.Services, CancellationToken.None);
await app.RunAsync();
