using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;

namespace MsgHatch.AdminUI;

/// <summary>
/// A column of the message types page, which another admin UI package adds with
/// <c>AddMessageTypeColumn</c> (D275). The page shows each registered column after its own columns, in the order of
/// registration. A column resolves only the query interfaces of <c>TenantKey.None</c>.
/// </summary>
public interface IMessageTypeColumn
{
    /// <summary>The header of the column, for example <c>Subscriptions</c>.</summary>
    string Header { get; }

    /// <summary>Gives the items of the cell of each message type in one call. A message type with no entry has an empty cell.</summary>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The items of each cell, or the error, which the page shows above the list with the header.</returns>
    ValueTask<ErrorOr<IReadOnlyDictionary<MessageType, IReadOnlyList<MessageTypeCellItem>>>> GetCellsAsync(CancellationToken ct);
}

/// <summary>One badge in a cell of an <see cref="IMessageTypeColumn"/> (D275).</summary>
/// <param name="Text">The text of the badge, for example <c>Company 2</c>.</param>
/// <param name="Href">The address of the badge as a link, relative to the base address of the host, or null for no link.</param>
public sealed record MessageTypeCellItem(string Text, string? Href);
