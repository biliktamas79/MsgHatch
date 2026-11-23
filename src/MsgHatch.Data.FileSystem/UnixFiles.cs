using System.Runtime.InteropServices;

namespace MsgHatch.Data.FileSystem;

/// <summary>The file calls of the C library of Linux and macOS that .NET does not give (D312).</summary>
internal static partial class UnixFiles
{
    // EEXIST is 17 on Linux and macOS.
    private const int _fileExists = 17;

    /// <summary>Gives a file a second name with <c>link()</c>, which fails in the same call of the kernel when the name exists.</summary>
    /// <returns>True when it created the name, or false when the name exists.</returns>
    /// <exception cref="IOException">Each other error, for example a file system with no hard links.</exception>
    public static bool TryLink(string existing, string created)
    {
        if (Link(existing, created) == 0)
        {
            return true;
        }

        var error = Marshal.GetLastPInvokeError();
        if (error != _fileExists)
        {
            throw new IOException($"link() gave the error {error}: {Marshal.GetPInvokeErrorMessage(error)}");
        }

        return false;
    }

    [LibraryImport("libc", EntryPoint = "link", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int Link(string existing, string created);
}
