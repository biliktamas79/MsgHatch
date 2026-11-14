using System.Net.Sockets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Dispatch;

namespace MsgHatch.Dispatch.NamedPipes.DependencyInjection;

/// <summary>
/// The startup check of the pipe paths on Linux and macOS: the pipe path of each topic of the host fits the limit of the
/// platform. The topics are the inbox and each channel with a registered transport.
/// </summary>
internal sealed class PipePathValidator(IServiceProvider services) : IValidateOptions<NamedPipeSignalOptions>
{
    public ValidateOptionsResult Validate(string? name, NamedPipeSignalOptions options)
    {
        if (OperatingSystem.IsWindows())
        {
            return ValidateOptionsResult.Skip;
        }

        string[] failures =
        [
            .. TopicsOfTheHost()
                .Select(topic => (topic, path: PipeNames.UnixSocketPathOf(PipeNames.Of(options.PipeNamePrefix, topic))))
                .Where(static pipe => !FitsThePlatform(pipe.path))
                .Select(static pipe =>
                    $"UseNamedPipeDispatchSignal gives the topic {pipe.topic.Name} the pipe path {pipe.path}, which is too long for this platform."),
        ];
        return failures.Length == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private IEnumerable<DispatchTopic> TopicsOfTheHost() =>
        [DispatchTopic.Inbox, .. services.GetServices<ITransport>().Select(static transport => DispatchTopic.ForChannel(transport.Channel))];

    private static bool FitsThePlatform(string path)
    {
        try
        {
            _ = new UnixDomainSocketEndPoint(path);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }
}
