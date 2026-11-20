using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Management;

namespace MsgHatch.AdminUI;

/// <summary>
/// The state of a delivery list on the pages of the server (D112, D295): the shown page, the cursors of the pages before it
/// for Newer, a read after a pause for a text filter, only the answer of the last read, and the result of the last retry or
/// cancel.
/// </summary>
/// <typeparam name="TItem">The summary of a message.</typeparam>
/// <param name="read">Reads the page after a cursor, or the first page for null, with the filters of the page.</param>
/// <param name="clock">The clock of the pause.</param>
internal sealed class DeliveryListState<TItem>(Func<MessageCursor?, Task<ErrorOr<MessagePage<TItem>>>> read, TimeProvider clock)
{
    private static readonly TimeSpan _pause = TimeSpan.FromMilliseconds(500);
    private readonly Stack<MessageCursor?> _before = new();
    private MessageCursor? _current;
    private MessageCursor? _next;
    private int _lastLoad;

    /// <summary>The items of the shown page, an error, or null while the page reads them.</summary>
    public ErrorOr<IReadOnlyList<TItem>>? Items { get; private set; }

    /// <summary>The result of the last retry or cancel, or null.</summary>
    public ErrorOr<DeliveryActionResult>? Result { get; private set; }

    /// <summary>True when a newer page is before the shown page.</summary>
    public bool HasNewer => _before.Count > 0;

    /// <summary>True when the server gave the cursor of an older page.</summary>
    public bool HasOlder => _next is not null;

    /// <summary>Reads the first page, for example after a change of a filter, and hides the last result.</summary>
    public Task LoadFirstPageAsync()
    {
        _before.Clear();
        (_current, Result) = (null, null);
        return LoadAsync();
    }

    /// <summary>Reads the first page after a pause of 500 ms, only when no other read starts in the pause.</summary>
    public async Task LoadAfterAPauseAsync()
    {
        var load = ++_lastLoad;
        await Task.Delay(_pause, clock);
        if (load == _lastLoad)
        {
            await LoadFirstPageAsync();
        }
    }

    /// <summary>Reads the shown page again.</summary>
    public async Task LoadAsync()
    {
        var load = ++_lastLoad;
        (Items, _next) = (null, null);
        var page = await read(_current);
        if (load != _lastLoad)
        {
            return;
        }

        Items = page.IsError ? page.Error : ErrorOr<IReadOnlyList<TItem>>.ForValue(page.Value.Items);
        _next = page.IsError ? null : page.Value.Next;
    }

    /// <summary>Reads the page after the cursor of the last message of the shown page.</summary>
    public Task OlderAsync()
    {
        if (_next is { } next)
        {
            _before.Push(_current);
            _current = next;
        }

        return LoadAsync();
    }

    /// <summary>Reads the page before the shown page again.</summary>
    public Task NewerAsync()
    {
        if (_before.Count > 0)
        {
            _current = _before.Pop();
        }

        return LoadAsync();
    }

    /// <summary>Keeps the result of a retry or a cancel, and reads the shown page again.</summary>
    public async Task ShowResultAsync(ErrorOr<DeliveryActionResult> result)
    {
        Result = result;
        await LoadAsync();
    }
}
