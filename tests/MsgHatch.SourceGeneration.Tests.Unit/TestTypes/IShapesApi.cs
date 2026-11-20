using MsgHatch.Domain.Failures;
using MsgHatch.Http;
using MsgHatch.Templates;
using MsgHatch.Templates.Api.Contracts;

namespace MsgHatch.SourceGeneration.Tests.Unit.TestTypes;

/// <summary>An HTTP API with each supported shape. The generator writes its client <c>ShapesApi</c> into this project (D236).</summary>
[HttpApi(typeof(TemplateEndpointJson))]
internal interface IShapesApi
{
    [HttpCall("GET", "/items/{name}/{id:guid}/{count}/{flag}/{kind}")]
    Task<ErrorOr<TemplateEntryResponse>> GetAsync(
        string name, Guid id, int count, bool flag, TemplatePartKind kind,
        string? text, Guid? key, long? number, bool? enabled, TemplatePartKind? part, decimal amount, CancellationToken ct);

    [HttpCall("PUT", "/items")]
    Task<ErrorOr<TemplateEntryResponse>> SaveAsync([HttpBody] TemplateSaveRequest body, CancellationToken ct);

    [HttpCall("DELETE", "")]
    Task<Error?> DeleteAsync(string? tenantId);

    [HttpCall("GET", "/lists")]
    Task<Error?> ListAsync(IReadOnlyList<Guid>? ids, IReadOnlyList<string> names, DateTime? from, CancellationToken ct);
}
