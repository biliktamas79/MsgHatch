using MsgHatch.Domain.Management;

namespace MsgHatch.AdminUI;

/// <summary>
/// The pages of a delivery list on the cursors of the server (D112, D295): the cursor of the shown page, and the cursors of
/// the pages before it, so that Newer goes back to the page before.
/// </summary>
internal sealed class CursorPages
{
    private readonly Stack<MessageCursor?> _before = new();

    /// <summary>The cursor of the shown page, or null for the first page.</summary>
    public MessageCursor? Current { get; private set; }

    /// <summary>True when a newer page is before the shown page.</summary>
    public bool HasNewer => _before.Count > 0;

    /// <summary>Goes back to the first page, for example after a change of a filter.</summary>
    public void Reset()
    {
        _before.Clear();
        Current = null;
    }

    /// <summary>Goes to the page after the cursor of the last message of the shown page.</summary>
    public void Older(MessageCursor next)
    {
        _before.Push(Current);
        Current = next;
    }

    /// <summary>Goes back to the page before the shown page.</summary>
    public void Newer()
    {
        if (_before.Count > 0)
        {
            Current = _before.Pop();
        }
    }
}
