namespace MsgHatch.Data.FileSystem.Tests.Unit.TestTypes;

/// <summary>Shared by the file store tests: a new temporary root folder, and the message files under it.</summary>
public static class StoreFolders
{
    public static string New() => Path.Combine(Path.GetTempPath(), "msghatch-tests", Guid.NewGuid().ToString("N"));

    /// <summary>The message files of one store, in its folder and in its test folder, in the order of their names.</summary>
    public static IEnumerable<string> MessageFiles(string rootPath, string store) =>
        new[] { Path.Combine(rootPath, store), Path.Combine(rootPath, "test", store) }
            .Where(Directory.Exists)
            .SelectMany(static folder => Directory.EnumerateFiles(folder, "*.json", SearchOption.AllDirectories))
            .Where(static path => Path.GetFileName(path) != "down.json")
            .OrderBy(Path.GetFileName, StringComparer.Ordinal);

    /// <summary>The folder of a file, relative to the root folder, for example <c>inbox/pending</c>.</summary>
    public static string RelativeFolderOf(string rootPath, string path) => Path.GetRelativePath(rootPath, Path.GetDirectoryName(path)!);

    public static void Delete(string rootPath)
    {
        if (Directory.Exists(rootPath))
        {
            Directory.Delete(rootPath, recursive: true);
        }

        if (File.Exists(rootPath))
        {
            File.Delete(rootPath);
        }
    }

    /// <summary>Puts a file where the root folder of a store must be, so that each folder operation of the store fails.</summary>
    public static void BlockWithAFile(string rootPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(rootPath)!);
        File.WriteAllText(rootPath, "not a folder");
    }
}
