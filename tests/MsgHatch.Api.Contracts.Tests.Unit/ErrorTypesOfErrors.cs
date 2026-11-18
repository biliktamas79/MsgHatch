using MsgHatch.Domain.Failures;

namespace MsgHatch.Api.Contracts.Tests.Unit;

/// <summary>Each error type of <see cref="Errors"/>, for the rows of the tests.</summary>
public static class ErrorTypesOfErrors
{
    public static IReadOnlyList<ErrorType> All { get; } = [.. typeof(Errors).GetFields().Select(static field => (ErrorType)field.GetValue(null)!)];

    public static ErrorType Named(string name) => All.Single(type => type.Name == name);
}
