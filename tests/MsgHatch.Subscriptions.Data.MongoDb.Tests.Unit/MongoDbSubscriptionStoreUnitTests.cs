using MongoDB.Bson;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Subscriptions.Tests.Unit.TestTypes;
using MsgHatch.Tests.Shared;

namespace MsgHatch.Subscriptions.Data.MongoDb.Tests.Unit;

/// <summary>
/// The decisions of <see cref="MongoDbSubscriptionStore"/> that need no server (D330), on a database of test doubles: the
/// owner and the recipients of a stored document, and the filters of a query and of a delivery. The behavior of a server
/// is in the integration tests.
/// </summary>
public sealed class MongoDbSubscriptionStoreUnitTests
{
    private const string _subscriptions = "MsgHatchSubscription";

    private static readonly Guid _id = new("11111111-1111-1111-1111-111111111111");

    private readonly DoubledDatabase _database = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<string, string, string?, string?, SubscriptionOwner> OwnerRows => new()
    {
        { "the default owner", "Default", null, null, SubscriptionOwner.Default },
        { "a company", "Company", "c1", null, SubscriptionOwner.ForCompany(new CompanyId("c1")) },
        { "a user", "User", null, "u1", SubscriptionOwner.ForUser(new UserId("u1")) },
    };

    public static TheoryData<string, string, string?, string?> OwnerThatDoesNotMatchItsLevelRows => new()
    {
        { "the default level with a company", "Default", "c1", null },
        { "the default level with a user", "Default", null, "u1" },
        { "the company level with no company", "Company", null, null },
        { "the company level with a user too", "Company", "c1", "u1" },
        { "the user level with no user", "User", null, null },
        { "the user level with a company too", "User", "c1", "u1" },
    };

    public static TheoryData<string, string, string, RecipientKind, string[]> RecipientRows => new()
    {
        { "two named users", "NamedUsers", "['u1', 'u2']", RecipientKind.NamedUsers, ["u1", "u2"] },
        { "the company", "Company", "null", RecipientKind.Company, [] },
        { "the users of the scope", "ScopeUsers", "null", RecipientKind.ScopeUsers, [] },
    };

    public static TheoryData<string, SubscriptionQuery, string> QueryFilterRows => new()
    {
        { "no filter", new SubscriptionQuery(), "{ tenant: null }" },
        { "a message type", new SubscriptionQuery(Type: new MessageType("EventA")), "{ tenant: null, type: 'EventA' }" },
        { "a company", new SubscriptionQuery(Company: new CompanyId("c1")), "{ tenant: null, company: 'c1' }" },
        { "a user", new SubscriptionQuery(User: new UserId("u1")), "{ tenant: null, user: 'u1' }" },
        { "a level", new SubscriptionQuery(Level: SubscriptionLevel.Company), "{ tenant: null, level: 'Company' }" },
    };

    [Theory]
    [MemberData(nameof(OwnerRows))]
    public async Task GetAsync_OwnerFieldsOfTheLevel_GivesTheOwnerOfTheRow(string row, string level, string? company, string? user, SubscriptionOwner expected)
    {
        // Arrange
        _database.Finds(_subscriptions, AStoredSubscription(level, company, user));

        // Act
        var subscription = await Store().GetAsync(_id, Ct);

        // Assert
        Assert.Equal((row, (SubscriptionOwner?)expected), (row, subscription.Value?.Owner));
    }

    [Theory]
    [MemberData(nameof(OwnerThatDoesNotMatchItsLevelRows))]
    public async Task GetAsync_OwnerFieldsThatDoNotMatchTheLevel_ReturnsSerializationFailed(string row, string level, string? company, string? user)
    {
        // Arrange
        _database.Finds(_subscriptions, AStoredSubscription(level, company, user));

        // Act
        var subscription = await Store().GetAsync(_id, Ct);

        // Assert
        Assert.Equal((row, (ErrorType?)Errors.SerializationFailed), (row, subscription.Error?.Type));
    }

    [Theory]
    [MemberData(nameof(RecipientRows))]
    public async Task GetAsync_RecipientsOfTheDocument_GivesTheRecipientsOfTheRow(
        string row, string recipients, string namedUsers, RecipientKind expectedKind, string[] expectedUsers)
    {
        // Arrange
        _database.Finds(_subscriptions, AStoredSubscription(recipients: recipients, namedUsers: namedUsers));

        // Act
        var subscription = await Store().GetAsync(_id, Ct);

        // Assert
        var read = subscription.Value!.Recipients;
        Assert.Equal((row, expectedKind, string.Join(',', expectedUsers)), (row, read.Kind, string.Join(',', read.Users.Select(static user => user.Value))));
    }

    [Fact]
    public async Task GetAsync_NamedUsersThatAreNoArray_ReturnsSerializationFailed()
    {
        // Arrange
        _database.Finds(_subscriptions, AStoredSubscription(recipients: "NamedUsers", namedUsers: "'u1'")); // not an array

        // Act
        var subscription = await Store().GetAsync(_id, Ct);

        // Assert
        Assert.Equal(Errors.SerializationFailed, subscription.Error?.Type);
    }

    [Theory]
    [MemberData(nameof(QueryFilterRows))]
    public async Task QueryAsync_FiltersOfTheQuery_FindsWithTheFilterOfTheRow(string row, SubscriptionQuery query, string expected)
    {
        // Act
        await Store().QueryAsync(query, Ct);

        // Assert
        var find = Assert.Single(_database.FindsOf(_subscriptions));
        Assert.Equal((row, BsonDocument.Parse(expected).ToJson()), (row, DoubledDatabase.Rendered(find.Filter).ToJson()));
    }

    [Fact]
    public async Task FindForDeliveryAsync_CompaniesAndUsers_FindsTheDefaultsAndTheSubscriptionsOfTheCompaniesAndTheUsers()
    {
        // Act
        await Store().FindForDeliveryAsync(new MessageType("EventA"), [new CompanyId("c1")], [new UserId("u1"), new UserId("u2")], Ct);

        // Assert
        var find = Assert.Single(_database.FindsOf(_subscriptions));
        Assert.Equal(
            BsonDocument.Parse(
                "{ tenant: null, type: 'EventA', $or: [{ level: 'Default' }, { level: 'Company', company: { $in: ['c1'] } }, "
                + "{ level: 'User', user: { $in: ['u1', 'u2'] } }] }").ToJson(),
            DoubledDatabase.Rendered(find.Filter).ToJson());
    }

    private MongoDbSubscriptionStore Store() => new(_database.Database, TenantKey.None, ScenarioContentTypes.SettingsJson);

    private static BsonDocument AStoredSubscription(
        string level = "Default", string? company = null, string? user = null, string recipients = "ScopeUsers", string namedUsers = "null") =>
        BsonDocument.Parse(
            $$"""
            {
                _id: '11111111-1111-1111-1111-111111111111', revision: 1, tenant: null, level: '{{level}}',
                company: {{Text(company)}}, user: {{Text(user)}}, type: 'EventA', kind: 'OptOut', channel: null,
                settingsType: null, settingsVersion: null, settings: null, recipients: '{{recipients}}', namedUsers: {{namedUsers}},
                mandatory: false,
                createdAtUtc: { d: ISODate('2030-01-01T00:00:00Z'), t: NumberLong('640290528000000000') },
                lastModifiedAtUtc: { d: ISODate('2030-01-01T00:00:00Z'), t: NumberLong('640290528000000000') }
            }
            """);

    private static string Text(string? value) => value is null ? "null" : $"'{value}'";
}
