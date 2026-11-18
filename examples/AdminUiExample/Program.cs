using AdminUiExample;

Console.Title = "AdminUiExample";
await AdminUiApplication.Build(WebApplication.CreateBuilder(args)).RunAsync();
