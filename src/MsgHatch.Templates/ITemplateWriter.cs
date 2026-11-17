using MsgHatch.Domain.Failures;

namespace MsgHatch.Templates;

/// <summary>
/// Lists, saves, and deletes the templates of one template store (D218). A store is writable when it implements this
/// interface and <see cref="ITemplateStore"/>, so a read-only store, for example the embedded store, needs no change. A
/// save gives a new version, so the parse caches of the engines parse the new text (D223). A caller must not await one
/// returned <see cref="ValueTask{TResult}"/> two times.
/// </summary>
public interface ITemplateWriter
{
    /// <summary>Lists the templates that match all given filters of a query.</summary>
    /// <param name="query">The filters.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>One entry for each template, or the error of the storage.</returns>
    ValueTask<ErrorOr<IReadOnlyList<TemplateEntry>>> ListAsync(TemplateQuery query, CancellationToken ct);

    /// <summary>Saves the text of one template, when its stored version is the expected version.</summary>
    /// <param name="lookup">The message type, the language, the template scope, the part, and its kind.</param>
    /// <param name="text">The text.</param>
    /// <param name="expectedVersion">The version that the caller read, or 0 for a new template.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The entry with the new version, <see cref="Errors.Conflict"/> for another stored version, or the error of the storage.</returns>
    ValueTask<ErrorOr<TemplateEntry>> SaveAsync(TemplateLookup lookup, string text, long expectedVersion, CancellationToken ct);

    /// <summary>Deletes one template. A missing template is not an error.</summary>
    /// <param name="lookup">The message type, the language, the template scope, the part, and its kind.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>Null, or the error of the storage.</returns>
    ValueTask<Error?> DeleteAsync(TemplateLookup lookup, CancellationToken ct);
}
