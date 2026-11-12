using ExampleApp.Recipients;
using MsgHatch.Subscriptions.Tests.Unit.Contracts;

namespace ExampleApp.Tests.Unit.Recipients;

/// <summary>The directory contract (U1 to U4) on the SQLite tables of the example directory.</summary>
public sealed class ExampleRecipientDirectoryUnitTests : RecipientDirectoryTestsBase<ExampleRecipientDirectory>
{
    public ExampleRecipientDirectoryUnitTests()
        : base(ExampleRecipientDirectoryTestData.NewDirectory(out var testData), testData)
    {
    }
}
