using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Persistence;
using NSubstitute;

namespace MsgHatch.Domain.Tests.Unit.TestTypes;

/// <summary>The content type entries of the test types, and a registry substitute with them.</summary>
public static class TestContentTypes
{
    public static readonly ContentTypeEntry TestEventV2 = new(
        "TestEvent", 2, "TestEvent@2", typeof(ReceivedMessage<TestEvent>), TestJsonContext.Default.TestEvent);

    public static readonly ContentTypeEntry TestEventV1 = new(
        "TestEvent", 1, "TestEvent@1", typeof(ReceivedMessage<TestEventV1>), TestJsonContext.Default.TestEventV1);

    public static readonly ContentTypeEntry TestEnvelopeV1 = new(
        "Test:TestEnvelope", 1, "Test:TestEnvelope@1", typeof(OutgoingMessage<TestEnvelope>),
        TestJsonContext.Default.TestEnvelope);

    public static readonly ContentTypeEntry TestTraceV1 = new(
        "Trace:Test", 1, "Trace:Test@1", typeof(TestTrace), TestJsonContext.Default.TestTrace);

    public static IContentTypeRegistry ARegistry()
    {
        ContentTypeEntry[] entries = [TestEventV2, TestEventV1, TestEnvelopeV1, TestTraceV1];
        var registry = Substitute.For<IContentTypeRegistry>();
        registry.Entries.Returns(entries);
        registry.TryFind(Arg.Any<string>(), Arg.Any<int>(), out Arg.Any<ContentTypeEntry>()!)
            .Returns(call =>
            {
                var match = entries.FirstOrDefault(e => e.ContentType == (string)call[0] && e.Version == (int)call[1]);
                call[2] = match;
                return match is not null;
            });
        return registry;
    }
}
