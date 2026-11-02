using Microsoft.CodeAnalysis;
using MsgHatch.SourceGeneration.Tests.Unit.TestTypes;

namespace MsgHatch.SourceGeneration.Tests.Unit;

/// <summary>
/// The tests of the placeholder writer generator (section 4.10): its output through <c>CSharpGeneratorDriver</c>, and
/// the generated writer of <see cref="WriterTestPayload"/>, which the generator writes as an analyzer of this project.
/// </summary>
public sealed partial class PlaceholderPayloadGeneratorUnitTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Generate_PayloadTypesThatAreNotPartial_ReportMsgHatch001AsAnErrorForEachType()
    {
        // Arrange
        const string source = """
            using MsgHatch.Email.Placeholders;

            namespace Consumer;

            [PlaceholderPayload]
            public sealed record OrderShippedEmail(string CustomerName);

            [PlaceholderPayload]
            public sealed class PasswordResetEmail { public string? Link { get; set; } }
            """;

        // Act
        var run = GeneratorRun.Of(source, Ct);

        // Assert
        Assert.Equal(
            [("MSGHATCH001", DiagnosticSeverity.Error, 5), ("MSGHATCH001", DiagnosticSeverity.Error, 8)],
            run.GeneratorDiagnostics.Select(static d => (d.Id, d.Severity, d.Location.GetLineSpan().StartLinePosition.Line)));
    }

    [Fact]
    public void Generate_PayloadTypeThatIsNotPartial_AddsNoWriter()
    {
        // Arrange
        const string source = """
            using MsgHatch.Email.Placeholders;

            [PlaceholderPayload]
            public sealed record OrderShippedEmail(string CustomerName);
            """;

        // Act
        var run = GeneratorRun.Of(source, Ct);

        // Assert
        Assert.Empty(run.HintNames);
    }

    [Fact]
    public void Generate_PartialPayloadTypes_AddsAWriterForEachTypeThatCompilesWithNoError()
    {
        // Arrange
        const string source = """
            using System;
            using MsgHatch.Email.Placeholders;

            [PlaceholderPayload]
            public sealed partial class GlobalPayload { public string? Name { get; set; } }

            namespace Consumer
            {
                [PlaceholderPayload]
                public sealed partial record OrderShippedEmail(
                    string CustomerName, string? Note, int Count, int? Rank, decimal? Total, DateTime ShippedAt, bool Express,
                    bool? Gift, Version? AppVersion, Uri Link, object? Extra)
                {
                    public string @class { get; init; } = "";
                    public static string Ignored => "";
                    public string this[int index] => "";
                    private string Hidden { get; } = "";
                    public string WriteOnly { set { } }
                }

                public abstract record BasePayload(string BaseName);

                [PlaceholderPayload]
                public sealed partial record DerivedPayload(string Name) : BasePayload(Name);

                [PlaceholderPayload]
                public sealed partial class GenericPayload<T> { public T? Value { get; set; } }

                public static partial class InClass { [PlaceholderPayload] public sealed partial record Payload(string Name); }

                public partial struct InStruct { [PlaceholderPayload] public sealed partial class Payload { public int Count { get; set; } } }

                public partial record struct InRecordStruct { [PlaceholderPayload] public sealed partial class Payload { public int Count { get; set; } } }

                public partial record InRecord { [PlaceholderPayload] public sealed partial class Payload { public int Count { get; set; } } }

                public partial interface IInInterface { [PlaceholderPayload] public sealed partial class Payload { public int Count { get; set; } } }
            }
            """;

        // Act
        var run = GeneratorRun.Of(source, Ct);

        // Assert
        Assert.Empty(run.GeneratorDiagnostics);
        Assert.Equal([], run.CompileErrors.Select(static error => error.ToString()));
        Assert.Equal(
            [
                "Consumer.DerivedPayload.PlaceholderWriter.g.cs",
                "Consumer.GenericPayload_T_.PlaceholderWriter.g.cs",
                "Consumer.IInInterface.Payload.PlaceholderWriter.g.cs",
                "Consumer.InClass.Payload.PlaceholderWriter.g.cs",
                "Consumer.InRecord.Payload.PlaceholderWriter.g.cs",
                "Consumer.InRecordStruct.Payload.PlaceholderWriter.g.cs",
                "Consumer.InStruct.Payload.PlaceholderWriter.g.cs",
                "Consumer.OrderShippedEmail.PlaceholderWriter.g.cs",
                "GlobalPayload.PlaceholderWriter.g.cs",
            ],
            run.HintNames.Order(StringComparer.Ordinal));
    }
}
