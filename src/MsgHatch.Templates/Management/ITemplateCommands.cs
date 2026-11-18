using MsgHatch.Domain.Failures;

namespace MsgHatch.Templates.Management;

/// <summary>
/// The changes of the template administration (D222, D231, section 4.22). It is a tenant service (D126): the commands of
/// <c>TenantKey.None</c> use the shared template store, and the commands of a tenant use only the template store of that
/// tenant. They validate each request, and check a text with the engines of the message type before a save (D219, D220).
/// A caller must not await one returned <see cref="ValueTask{TResult}"/> two times.
/// </summary>
public interface ITemplateCommands
{
    /// <summary>Checks the text of one template with the engines of its message type, and then saves it.</summary>
    /// <param name="lookup">The message type, the language, the template scope, the part, and its kind.</param>
    /// <param name="text">The text.</param>
    /// <param name="expectedVersion">The version that the caller read, or 0 for a new template.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>
    /// The entry with the new version, <see cref="Errors.ValidationFailed"/>, <see cref="Errors.NotFound"/>,
    /// <see cref="Errors.TemplateInvalid"/>, <see cref="Errors.Conflict"/>, or the error of the store.
    /// </returns>
    ValueTask<ErrorOr<TemplateEntry>> SaveTemplateAsync(TemplateLookup lookup, string text, long expectedVersion, CancellationToken ct);

    /// <summary>Deletes one template. A missing template is not an error.</summary>
    /// <param name="lookup">The message type, the language, the template scope, the part, and its kind.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>Null, <see cref="Errors.ValidationFailed"/>, <see cref="Errors.NotFound"/> for no store, or the error of the store.</returns>
    ValueTask<Error?> DeleteTemplateAsync(TemplateLookup lookup, CancellationToken ct);
}
