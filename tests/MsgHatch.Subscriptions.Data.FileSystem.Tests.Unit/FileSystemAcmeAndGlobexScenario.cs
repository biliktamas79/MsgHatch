using MsgHatch.Domain.Identity;
using MsgHatch.Subscriptions.Data.FileSystem.Tests.Unit.TestTypes;
using MsgHatch.Subscriptions.Tests.Unit.Contracts;

namespace MsgHatch.Subscriptions.Data.FileSystem.Tests.Unit;

public sealed class FileSystemAcmeAndGlobexScenario()
    : AcmeAndGlobexScenario<FileSystemSubscriptionStore>(
        FileSystemSubscriptionStoreTestData.StoresOn(FileSystemSubscriptionStoreTestData.NewRootPath())(TenantKey.None),
        static store => new FileSystemSubscriptionStoreTestData(store.RootPath));
