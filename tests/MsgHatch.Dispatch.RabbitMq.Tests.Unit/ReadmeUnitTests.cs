using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MsgHatch.Dispatch.RabbitMq.DependencyInjection;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using MsgHatch.Tests.Shared;

namespace MsgHatch.Dispatch.RabbitMq.Tests.Unit;

/// <summary>The examples of the README of the package run as tests, and the README check compares them (D311).</summary>
public sealed class ReadmeUnitTests
{
    [Fact]
    public void Readme_Snippets_EqualTheirSources() => ReadmeCheck.AssertSnippets();

    [Fact]
    public void Readme_OptionsTables_NameEachOption() => ReadmeCheck.AssertOptionsTables(typeof(RabbitMqSignalOptions));

    [Fact]
    public async Task UseRabbitMqDispatchSignal_SettingsOfTheReadme_BindsTheBroker()
    {
        // Arrange
        var services = TestHost.CreateServices(configuration: ReadmeCheck.ReadSettings("options.json"));
        services.AddMsgHatch().UseInMemoryStores();

        // Act
        #region readme:setup
        services.AddMsgHatch()
            .UseRabbitMqDispatchSignal();
        #endregion
        await using var provider = services.Build();

        // Assert
        var options = provider.GetRequiredService<IOptions<RabbitMqSignalOptions>>().Value;
        Assert.Equal(new Uri("amqp://127.0.0.1:5672"), options.Uri);
        Assert.Equal("msghatch.dispatch", options.ExchangeName);
    }

    [Fact]
    public async Task UseRabbitMqDispatchSignal_ExchangeInCode_UsesTheValueOfTheCode()
    {
        // Arrange
        var services = TestHost.CreateServices(configuration: ReadmeCheck.ReadSettings("options.json"));
        services.AddMsgHatch().UseInMemoryStores();

        // Act
        #region readme:setup-code
        services.AddMsgHatch()
            .UseRabbitMqDispatchSignal(o => o.ExchangeName = "shop.dispatch");
        #endregion
        await using var provider = services.Build();

        // Assert
        Assert.Equal("shop.dispatch", provider.GetRequiredService<IOptions<RabbitMqSignalOptions>>().Value.ExchangeName);
    }
}
