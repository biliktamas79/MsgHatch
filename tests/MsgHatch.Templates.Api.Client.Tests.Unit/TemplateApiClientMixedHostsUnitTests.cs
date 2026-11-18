using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Identity;
using MsgHatch.Templates.Api.Client.DependencyInjection;
using MsgHatch.Templates.Api.Client.Tests.Unit.TestTypes;
using MsgHatch.Templates.DependencyInjection;
using MsgHatch.Templates.Management;
using MsgHatch.Templates.Placeholders;
using MsgHatch.Templates.Tests.Unit.TestTypes;

namespace MsgHatch.Templates.Api.Client.Tests.Unit;

/// <summary>
/// A host that reads over HTTP and writes in process, and the reverse, on the template folder of the server (D231): a write
/// of one side gives the new version to a read of the other side, and only the selected interface sends requests.
/// </summary>
/// <param name="folder">The temporary template folder of the test class, which the server and the client host share.</param>
public sealed class TemplateApiClientMixedHostsUnitTests(TemplateFolder folder) : IClassFixture<TemplateFolder>
{
    private readonly MessageType _type = new(TestContext.Current.TestMethod!.MethodName);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ReadsOverHttpAndWritesInProcess_TwoWritesInProcess_EachReadOverHttpGivesTheNewVersion()
    {
        // Arrange
        var log = new RequestLog();
        await using var host = await HttpManagementHost.StartAsync<ITemplateQueries>(
            services => ServicesOnTheFolder(services),
            (services, configureHttp) => ServicesOnTheFolder(services).UseTemplateQueriesApiClient(HttpManagementHost.BaseAddress, Logged(configureHttp, log)));
        var commands = host.Client.GetRequiredService<ITemplateCommands>();
        var queries = host.Service(TenantKey.None);

        // Act
        var first = await commands.SaveTemplateAsync(ALookup(), "First", 0, Ct);
        var firstRead = await queries.GetTemplateAsync(ALookup(), Ct);
        var second = await commands.SaveTemplateAsync(ALookup(), "Second", first.Value.Version, Ct);
        var secondRead = await queries.GetTemplateAsync(ALookup(), Ct);

        // Assert
        var path = $"/templates/{_type.Name}/en/subject/PlainText";
        Assert.Equal(
            ("First", first.Value.Version, "Second", second.Value.Version, $"{path}, {path}"),
            (firstRead.Value.Text, firstRead.Value.Key.Version, secondRead.Value.Text, secondRead.Value.Key.Version, Paths(log)));
    }

    [Fact]
    public async Task WritesOverHttpAndReadsInProcess_WriteOverHttp_ReadInProcessGivesTheNewVersion()
    {
        // Arrange
        var log = new RequestLog();
        await using var host = await HttpManagementHost.StartAsync<ITemplateCommands>(
            services => ServicesOnTheFolder(services),
            (services, configureHttp) => ServicesOnTheFolder(services).UseTemplateCommandsApiClient(HttpManagementHost.BaseAddress, Logged(configureHttp, log)));
        var commands = host.Service(TenantKey.None);
        var queries = host.Client.GetRequiredService<ITemplateQueries>();

        // Act
        var saved = await commands.SaveTemplateAsync(ALookup(), "Over HTTP", 0, Ct);
        var read = await queries.GetTemplateAsync(ALookup(), Ct);

        // Assert
        Assert.Equal(
            ("Over HTTP", saved.Value.Version, $"/templates/{_type.Name}/en/subject/PlainText"),
            (read.Value.Text, read.Value.Key.Version, Paths(log)));
    }

    private static Action<IHttpClientBuilder> Logged(Action<IHttpClientBuilder> configureHttp, RequestLog log) =>
        http =>
        {
            configureHttp(http);
            http.AddHttpMessageHandler(log.Handler);
        };

    private static string Paths(RequestLog log) => string.Join(", ", log.Uris.Select(static uri => uri.PathAndQuery));

    private MsgHatchBuilder ServicesOnTheFolder(IServiceCollection services) =>
        services.AddTestServices().AddMsgHatch(static o => o.DefaultLanguage = "en")
            .UseFileTemplates(folder.Root)
            .AddOrderEmailEngine(PlaceholderTemplates.Engine<OrderEmail>, _type.Name);

    private TemplateLookup ALookup() => new(_type, "en", Scope: null, "subject", TemplatePartKind.PlainText);
}
