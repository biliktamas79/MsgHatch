using System.ComponentModel.DataAnnotations;

namespace MsgHatch.Data.MongoDb;

/// <summary>The settings of the MongoDB stores (section 4.4, D121).</summary>
public sealed class MongoDbStoreOptions
{
    /// <summary>
    /// The key of the section under the root of <c>AddMsgHatch</c> (D329): <c>{root}:MongoDb</c>, by default
    /// <c>MsgHatch:MongoDb</c>.
    /// </summary>
    public const string SectionKey = "MongoDb";

    /// <summary>The default name of the database.</summary>
    public const string DefaultDatabaseName = "msghatch";

    /// <summary>
    /// The connection string of the MongoDB server, for example <c>mongodb://127.0.0.1:27017</c>. The setup method makes
    /// the <c>IMongoClient</c> from it when the host has not registered one.
    /// </summary>
    [Required]
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// The database of the stores, for example <c>msghatch</c>. It contains only ASCII letters, digits, <c>_</c>, and
    /// <c>-</c>, and has at most 63 characters, the limit of the server.
    /// </summary>
    [Required]
    [RegularExpression("^[A-Za-z0-9_-]+$")]
    [MaxLength(63)]
    public string DatabaseName { get; set; } = DefaultDatabaseName;
}
