using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using WolverineExample.UserSimulator;

Console.Title = "WolverineExample.UserSimulator";
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = args, ContentRootPath = AppContext.BaseDirectory });
builder.Configuration.AddUserSecrets<Program>(optional: true);
builder.Services.AddUserSimulator();
await builder.Build().RunAsync();
