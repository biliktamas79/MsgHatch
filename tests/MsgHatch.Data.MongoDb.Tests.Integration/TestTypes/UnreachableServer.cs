using MongoDB.Driver;

namespace MsgHatch.Data.MongoDb.Tests.Integration.TestTypes;

/// <summary>A database on a port with no server, with a server selection timeout of half a second.</summary>
public static class UnreachableServer
{
    private static readonly MongoClient _client = new("mongodb://localhost:1/?serverSelectionTimeoutMS=500&connectTimeoutMS=500");

    public static IMongoDatabase Database => _client.GetDatabase("msghatch");
}
