namespace MsgHatch.Domain.Telemetry;

/// <summary>
/// The names of the traces and the metrics of MsgHatch (D131). A host turns them on in its OpenTelemetry setup with
/// <c>AddSource(MsgHatchTelemetry.SourceName)</c> and <c>AddMeter(MsgHatchTelemetry.MeterName)</c>.
/// The span names and the attributes follow the OpenTelemetry semantic conventions for messaging, version 1.44.0,
/// which have the status Development. A change of a name comes only in its own phase.
/// </summary>
public static class MsgHatchTelemetry
{
    /// <summary>The name of the <see cref="System.Diagnostics.ActivitySource"/> of MsgHatch.</summary>
    public const string SourceName = "MsgHatch";

    /// <summary>The name of the <see cref="System.Diagnostics.Metrics.Meter"/> of MsgHatch.</summary>
    public const string MeterName = "MsgHatch";
}
