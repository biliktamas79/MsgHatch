using System.Text.RegularExpressions;
using MsgHatch.Domain.Identity;
using MsgHatch.Subscriptions.Data.FileSystem.Tests.Unit.TestTypes;
using MsgHatch.Subscriptions.Tests.Unit.Contracts;
using MsgHatch.Subscriptions.Tests.Unit.TestTypes;

namespace MsgHatch.Subscriptions.Data.FileSystem.Tests.Unit;

public sealed class FileSystemSubscriptionStoreUnitTests(FileSystemAcmeAndGlobexScenario scenario)
    : SubscriptionStoreTestsBase<FileSystemSubscriptionStore>(
        FileSystemSubscriptionStoreTestData.StoresOn(FileSystemSubscriptionStoreTestData.NewRootPath()),
        static store => new FileSystemSubscriptionStoreTestData(store.RootPath),
        scenario),
      IClassFixture<FileSystemAcmeAndGlobexScenario>
{
    public static TheoryData<string, string, string, int> BrokenFileRows => new()
    {
        { "invalid JSON", "\"id\":", "\"id:", 415 },
        { "JSON null", @"(?s)\A.*\z", "null", 415 },
        { "missing property", "\"mandatory\": false,", "", 415 },
        { "owner that does not match its level", "\"level\": \"User\"", "\"level\": \"Company\"", 415 },
        { "invalid message type name", "\"type\": \"B\"", "\"type\": \"B B\"", 415 },
        { "newer schema version", "\"schemaVersion\": 1", "\"schemaVersion\": 2", 426 },
        { "newer settings version", "\"settingsVersion\": 1", "\"settingsVersion\": 2", 426 },
    };

    [Fact]
    public async Task AddAsync_UserWithCharactersThatAFileNameCannotHave_StoresTheFileUnderTheEscapedNameAndFindsItAgain()
    {
        // Arrange
        var subscription = Scenario.S6 with { Owner = SubscriptionOwner.ForUser(new UserId("auth0|abc")) };
        await Store.AddAsync(subscription, Ct);

        // Act
        var found = await Store.FindForDeliveryAsync(subscription.Type, [], [new UserId("auth0|abc")], Ct);

        // Assert
        Assert.Equal(
            (Path.Combine("subscriptions", "no-tenant", "B", "user", "auth0%7^cabc", $"{subscription.Id:D}.json"), subscription),
            (Path.GetRelativePath(Store.RootPath, Assert.Single(SubscriptionFiles())), Assert.Single(found.Value)));
    }

    [Fact]
    public async Task UpdateAsync_AnotherOwner_MovesTheFileToTheFolderOfTheNewOwner()
    {
        // Arrange
        await Store.AddAsync(Scenario.S6, Ct);
        var moved = Scenario.S6 with { Revision = 2, Owner = SubscriptionOwner.ForUser(new UserId("2")) };
        Assert.Null(await Store.UpdateAsync(moved, 1, Ct));

        // Act
        var files = SubscriptionFiles();

        // Assert
        Assert.Equal(
            Path.Combine("subscriptions", "no-tenant", "B", "user", "2", $"{moved.Id:D}.json"),
            Path.GetRelativePath(Store.RootPath, Assert.Single(files)));
    }

    [Fact]
    public async Task AddAsync_AnotherCallerHoldsTheLockOfTheOwnerFolder_GivesUnavailable()
    {
        // Arrange
        var ownerFolder = Path.Combine(Store.RootPath, "subscriptions", "no-tenant", "B", "user", "1");
        Directory.CreateDirectory(ownerFolder);
        await using var heldLock = new FileStream(Path.Combine(ownerFolder, ".lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

        // Act
        var error = await Store.AddAsync(Scenario.S6, Ct);

        // Assert
        Assert.Equal(503, CodeOf(error));
    }

    [Theory]
    [MemberData(nameof(BrokenFileRows))]
    public async Task GetAsync_FileThatAPersonBroke_GivesTheErrorOfTheRow(string row, string pattern, string replacement, int expectedCode)
    {
        // Arrange
        await Store.AddAsync(Scenario.S6, Ct);
        var path = Assert.Single(SubscriptionFiles());
        await File.WriteAllTextAsync(path, Regex.Replace(await File.ReadAllTextAsync(path, Ct), pattern, replacement), Ct);

        // Act
        var stored = await Store.GetAsync(Scenario.S6.Id, Ct);

        // Assert
        Assert.Equal((row, (int?)expectedCode), (row, stored.Error?.Type.Code));
    }

    [Fact]
    public async Task FindForDeliveryAsync_FileWithANewerSchemaVersion_GivesUnsupportedVersion()
    {
        // Arrange
        await Store.AddAsync(Scenario.S6, Ct);
        var path = Assert.Single(SubscriptionFiles());
        await File.WriteAllTextAsync(path, (await File.ReadAllTextAsync(path, Ct)).Replace("\"schemaVersion\": 1", "\"schemaVersion\": 2", StringComparison.Ordinal), Ct);

        // Act
        var found = await Store.FindForDeliveryAsync(Scenario.S6.Type, [], [new UserId("1")], Ct);

        // Assert
        Assert.Equal(426, found.Error?.Type.Code);
    }

    private string[] SubscriptionFiles() => Directory.GetFiles(Store.RootPath, "*.json", SearchOption.AllDirectories);
}
