using Microsoft.Extensions.Options;

namespace MsgHatch.Data.MongoDb;

/// <summary>The source-generated validator of <see cref="MongoDbStoreOptions"/> (D64).</summary>
[OptionsValidator]
public sealed partial class MongoDbStoreOptionsValidator : IValidateOptions<MongoDbStoreOptions>;
