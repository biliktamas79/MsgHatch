using MsgHatch.Domain.Dispatch;

namespace MsgHatch.Dispatch.NamedPipes;

/// <summary>The pipe of each topic (D192).</summary>
internal static class PipeNames
{
    public static string Of(string prefix, DispatchTopic topic) => $"{prefix}.{topic.Name.Replace(':', '.')}";

    // On Linux and macOS, .NET makes the pipe of a name the Unix domain socket {temp folder}CoreFxPipe_{name}
    // (checked facts of phase 21 in the roadmap).
    public static string UnixSocketPathOf(string pipeName) => Path.Combine(Path.GetTempPath(), "CoreFxPipe_" + pipeName);
}
