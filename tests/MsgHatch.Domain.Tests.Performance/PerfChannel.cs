using System.Text.Json.Serialization;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Routing;

namespace MsgHatch.Domain.Tests.Performance;

/// <summary>The channel of the domain benchmarks, with its settings, its envelope, its event, and its registration.</summary>
public static class PerfChannel
{
    public static readonly ChannelId Id = new("Perf");

    public static readonly ChannelDescriptor<PerfSettings> Descriptor = new(Id, "Benchmark channel", ChannelFeatures.None);

    public static readonly MessageType Type = new("PerfEvent");

    /// <summary>Registers the channel and the message type <c>PerfEvent</c> with its router and its composer.</summary>
    public static MsgHatchBuilder AddPerfMessage(this MsgHatchBuilder builder) =>
        builder
            .AddChannelType(Descriptor, 1, PerfJson.Default.PerfSettings, new PerfSettingsValidator())
            .AddMessage(Type.Name, 1, PerfJson.Default.PerfEvent, static m => m
                .WithRouter<PerfRouter>()
                .AsChannel<PerfEnvelope, PerfComposer>(Id, "PerfEnvelope", 1, PerfJson.Default.PerfEnvelope));
}

/// <summary>The settings of the benchmark channel.</summary>
public sealed record PerfSettings : ChannelSettings;

/// <summary>The envelope of the benchmark channel.</summary>
/// <param name="Text">The text.</param>
public sealed record PerfEnvelope(string Text);

/// <summary>The event of the benchmarks. Its router gives <paramref name="Routes"/> routes.</summary>
/// <param name="Routes">The number of routes.</param>
public sealed record PerfEvent(int Routes);

/// <summary>Accepts each settings value of the benchmark channel.</summary>
public sealed class PerfSettingsValidator : IChannelSettingsValidator
{
    public ChannelId Channel => PerfChannel.Id;

    public Failures.Error? Validate(ChannelSettings settings, ChannelFeatures features) => null;
}

[JsonSerializable(typeof(PerfSettings))]
[JsonSerializable(typeof(PerfEnvelope))]
[JsonSerializable(typeof(PerfEvent))]
internal sealed partial class PerfJson : JsonSerializerContext;
