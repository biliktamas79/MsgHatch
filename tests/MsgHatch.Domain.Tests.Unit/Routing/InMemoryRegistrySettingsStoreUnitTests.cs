using MsgHatch.Domain.Routing;
using MsgHatch.Domain.Tests.Unit.Contracts;

namespace MsgHatch.Domain.Tests.Unit.Routing;

public sealed class InMemoryRegistrySettingsStoreUnitTests()
    : RegistrySettingsStoreTestsBase<InMemoryRegistrySettingsStore>(
        new InMemoryRegistrySettingsStore(),
        static _ => new InMemoryRegistrySettingsStoreTestData());
