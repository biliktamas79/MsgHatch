using Microsoft.Extensions.Options;

namespace MsgHatch.Domain.Tests.Unit.TestTypes;

/// <summary>Shared by the options tests: reads the failed property names, never the failure text.</summary>
public static class OptionsFailures
{
    public static string[] FailedProperties(ValidateOptionsResult result) =>
        [.. (result.Failures ?? []).Select(static failure => failure[..failure.IndexOf(':', StringComparison.Ordinal)])];
}
