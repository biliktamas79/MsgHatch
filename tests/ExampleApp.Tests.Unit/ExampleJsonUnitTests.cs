using ExampleApp.Notices;
using ExampleApp.PasswordResets;
using System.Text.Json;

namespace ExampleApp.Tests.Unit;

/// <summary>
/// The golden-file tests of the example types (section 4.13): each released version of a type that the inbox or the
/// outbox stores keeps one JSON sample in <c>Samples/</c>, and the sample never changes after its release.
/// </summary>
public sealed class ExampleJsonUnitTests
{
    public static TheoryData<string, Type, string> SampleRows => new()
    {
        { "EventA.v1.json", typeof(EventA), "EventA users=1,2 company= text=Your report is ready." },
        { "EventB.v1.json", typeof(EventB), "EventB users=1 company= text=A new invoice is open." },
        { "EventC.v1.json", typeof(EventC), "EventC users=null company=100 text=The maintenance starts at 22:00." },
        { "EventD.v1.json", typeof(EventD), "EventD users=2,3 company= text=Árvíztűrő tükörfúrógép <&>" },
        { "EventE.v1.json", typeof(EventE), "EventE users= company=200 text=The weekly summary is ready." },
        { "PasswordReset.v1.json", typeof(PasswordReset), "PasswordReset { UserId = 1, Email = client1@acme.example, Language = en }" },
        { "Notice.v1.json", typeof(Notice), "Notice { Type = D, Text = A contract changed. }" },
    };

    [Theory]
    [MemberData(nameof(SampleRows))]
    public void Deserialize_GoldenSampleOfVersion1_GivesTheSampleValue(string fileName, Type type, string expected)
    {
        // Arrange
        var sample = ReadSample(fileName);

        // Act
        var value = JsonSerializer.Deserialize(sample, ExampleJson.Default.GetTypeInfo(type)!);

        // Assert
        Assert.Equal(expected, Describe(value));
    }

    private static string Describe(object? value) => value switch
    {
        IExampleEvent e => $"{e.GetType().Name} users={(e.UserIds is null ? "null" : string.Join(',', e.UserIds))} company={e.CompanyId} text={e.Text}",
        _ => $"{value}",
    };

    private static byte[] ReadSample(string fileName)
    {
        using var stream = typeof(ExampleJsonUnitTests).Assembly.GetManifestResourceStream($"ExampleApp.Tests.Unit.Samples.{fileName}")!;
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }
}
