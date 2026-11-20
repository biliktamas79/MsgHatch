using Microsoft.CodeAnalysis;
using MsgHatch.SourceGeneration.Tests.Unit.TestTypes;

namespace MsgHatch.SourceGeneration.Tests.Unit;

/// <summary>
/// The tests of the generator of the HTTP API clients (D236): its diagnostics and its output through
/// <c>CSharpGeneratorDriver</c>, and the generated client of <see cref="IShapesApi"/>, which the generator writes as an
/// analyzer of this project.
/// </summary>
public sealed partial class HttpApiClientGeneratorUnitTests
{
    private const string _usings = """
        using System;
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using MsgHatch.Domain.Failures;
        using MsgHatch.Http;
        using MsgHatch.Templates;
        using MsgHatch.Templates.Api.Contracts;

        namespace Consumer;

        """;

    private static readonly string[] _attributeFiles = ["Microsoft.CodeAnalysis.EmbeddedAttribute.cs", "MsgHatch.Http.Attributes.g.cs"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<string, string, string> DiagnosticRows => new()
    {
        { "a generic interface", "[HttpApi(typeof(TemplateEndpointJson))] internal interface ITestApi<T> { }", "MSGHATCH002" },
        { "a nested interface", "internal static class Outer { [HttpApi(typeof(TemplateEndpointJson))] internal interface ITestApi { } }", "MSGHATCH002" },
        { "a JSON context that is not a JsonSerializerContext", "[HttpApi(typeof(string))] internal interface ITestApi { }", "MSGHATCH003" },
        { "a property", Api("string Name { get; }"), "MSGHATCH004" },
        { "a method with no HttpCall", Api("Task<Error?> DeleteAsync(CancellationToken ct);"), "MSGHATCH004" },
        { "the HTTP method HEAD", Api("""[HttpCall("HEAD", "")] Task<Error?> HeadAsync(CancellationToken ct);"""), "MSGHATCH005" },
        { "a route value with no parameter", Api("""[HttpCall("GET", "/{id:guid}")] Task<Error?> GetAsync(CancellationToken ct);"""), "MSGHATCH006" },
        {
            "a second body",
            Api("""[HttpCall("POST", "")] Task<Error?> PostAsync([HttpBody] TemplateSaveRequest first, [HttpBody] TemplateSaveRequest second);"""),
            "MSGHATCH007"
        },
        { "a query value of a list", Api("""[HttpCall("GET", "")] Task<Error?> GetAsync(List<string> names, CancellationToken ct);"""), "MSGHATCH008" },
        { "a read-only list of an unsupported type", Api("""[HttpCall("GET", "")] Task<Error?> GetAsync(IReadOnlyList<Uri> uris, CancellationToken ct);"""), "MSGHATCH008" },
        { "a read-only list of nullable values", Api("""[HttpCall("GET", "")] Task<Error?> GetAsync(IReadOnlyList<Guid?> ids, CancellationToken ct);"""), "MSGHATCH008" },
        { "a read-only list as a route value", Api("""[HttpCall("GET", "/{ids}")] Task<Error?> GetAsync(IReadOnlyList<Guid> ids, CancellationToken ct);"""), "MSGHATCH008" },
        { "a nullable route value", Api("""[HttpCall("GET", "/{id}")] Task<Error?> GetAsync(Guid? id, CancellationToken ct);"""), "MSGHATCH008" },
        { "a token that is not the last parameter", Api("""[HttpCall("GET", "")] Task<Error?> GetAsync(CancellationToken ct, string name);"""), "MSGHATCH008" },
        { "a task of a string", Api("""[HttpCall("GET", "")] Task<string> GetAsync(CancellationToken ct);"""), "MSGHATCH009" },
        { "a value task", Api("""[HttpCall("DELETE", "")] ValueTask<Error?> DeleteAsync(CancellationToken ct);"""), "MSGHATCH009" },
        { "an error that is not nullable", Api("""[HttpCall("DELETE", "")] Task<Error> DeleteAsync(CancellationToken ct);"""), "MSGHATCH009" },
        { "a response type that is not in the context", Api("""[HttpCall("GET", "")] Task<ErrorOr<Uri>> GetAsync(CancellationToken ct);"""), "MSGHATCH010" },
        { "a body type that is not in the context", Api("""[HttpCall("POST", "")] Task<Error?> PostAsync([HttpBody] Uri body);"""), "MSGHATCH010" },
    };

    [Theory]
    [MemberData(nameof(DiagnosticRows))]
    public void Generate_InterfaceThatTheGeneratorCannotWrite_ReportsItsErrorAndAddsNoClient(string row, string source, string expected)
    {
        // Act
        var run = GeneratorRun.Of(_usings + source, new HttpApiClientGenerator(), Ct);

        // Assert
        Assert.Equal(
            (row, $"{expected} {DiagnosticSeverity.Error}", string.Join(", ", _attributeFiles)),
            (row, string.Join(", ", run.GeneratorDiagnostics.Select(static d => $"{d.Id} {d.Severity}")), Joined(run.HintNames)));
    }

    [Fact]
    public void Generate_InterfaceOfEachSupportedShape_AddsAClientThatCompilesWithNoErrorAndNoWarning()
    {
        // Arrange
        var source = _usings + """
            [HttpApi(typeof(TemplateEndpointJson))]
            internal interface IOrdersApi
            {
                [HttpCall("GET", "/{type}/{id:guid}/{count}/{flag}/{kind}")]
                Task<ErrorOr<TemplateResponse>> GetAsync(
                    string type, Guid id, int count, bool flag, TemplatePartKind kind, string? text, Guid? key, long? number, bool? enabled,
                    TemplatePartKind? part, double ratio, decimal amount, byte small, CancellationToken ct);

                [HttpCall("GET", "")]
                Task<ErrorOr<IReadOnlyList<TemplateEntryResponse>>> ListAsync(string? @class);

                [HttpCall("PUT", "/{type}")]
                Task<ErrorOr<TemplateEntryResponse>> SaveAsync(string type, [HttpBody] TemplateSaveRequest body, CancellationToken ct);

                [HttpCall("POST", "/check")]
                Task<Error?> CheckAsync([HttpBody] TemplateCheckRequest body, CancellationToken ct);

                [HttpCall("PATCH", "/{type}")]
                Task<Error?> PatchAsync(string type, [HttpBody] TemplateCheckRequest body);

                [HttpCall("DELETE", "/{type}/{type}")]
                Task<Error?> DeleteAsync(string type, CancellationToken ct);

                [HttpCall("GET", "/times/{at}")]
                Task<Error?> TimesAsync(
                    DateTime at, DateTime? from, IReadOnlyList<Guid>? ids, IReadOnlyList<TemplatePartKind> kinds, IReadOnlyList<DateTime>? times,
                    IReadOnlyList<string> names, CancellationToken ct);
            }

            [HttpApi(typeof(TemplateEndpointJson))]
            internal interface Orders
            {
            }
            """;

        // Act
        var run = GeneratorRun.Of(source, new HttpApiClientGenerator(), Ct);

        // Assert
        Assert.Equal(
            ("", "", "", $"Consumer.IOrdersApi.HttpClient.g.cs, Consumer.Orders.HttpClient.g.cs, {string.Join(", ", _attributeFiles)}"),
            (Joined(run.GeneratorDiagnostics), Joined(run.CompileErrors), Joined(run.CompileWarnings), Joined(run.HintNames)));
    }

    private static string Joined<T>(IEnumerable<T> values) =>
        string.Join(", ", values.Select(static value => value!.ToString()).Order(StringComparer.Ordinal));

    private static string Api(string members) => $$"""
        [HttpApi(typeof(TemplateEndpointJson))]
        internal interface ITestApi
        {
            {{members}}
        }
        """;
}
