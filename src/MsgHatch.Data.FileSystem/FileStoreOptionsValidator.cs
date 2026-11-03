using Microsoft.Extensions.Options;

namespace MsgHatch.Data.FileSystem;

/// <summary>The source-generated validator of <see cref="FileStoreOptions"/> (D64).</summary>
[OptionsValidator]
public sealed partial class FileStoreOptionsValidator : IValidateOptions<FileStoreOptions>;
