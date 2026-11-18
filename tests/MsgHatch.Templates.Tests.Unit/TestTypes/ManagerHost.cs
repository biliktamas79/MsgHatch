using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Domain.DependencyInjection;

namespace MsgHatch.Templates.Tests.Unit.TestTypes;

/// <summary>The services of the host of the template management tests, with the setup of each test (D222, D231).</summary>
public static class ManagerHost
{
    public static Action<IServiceCollection> Services(Action<MsgHatchBuilder> configure) =>
        services => configure(services.AddTestServices().AddMsgHatch(static o => o.DefaultLanguage = "en"));
}
