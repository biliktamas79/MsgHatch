using Microsoft.EntityFrameworkCore;

namespace MsgHatch.Data.EFCore;

/// <summary>The insert of one row on its unique key, with no SQL text (D318).</summary>
internal static class RowWrites
{
    /// <summary>
    /// Inserts one row, and gives false when the insert failed because the key of the row is stored, for example by a
    /// parallel caller. Each other failed insert throws. The context does not track the row after the call, so a caller
    /// can try again on the same context.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <param name="row">The new row.</param>
    /// <param name="isKeyStored">Reads whether the key of the row is stored.</param>
    /// <param name="ct">The cancel token.</param>
    public static async Task<bool> TryAddAsync<TRow>(DbContext context, TRow row, Func<Task<bool>> isKeyStored, CancellationToken ct)
        where TRow : class
    {
        var entry = context.Add(row);
        try
        {
            await context.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException)
        {
            if (await isKeyStored())
            {
                return false;
            }

            throw;
        }
        finally
        {
            entry.State = EntityState.Detached;
        }
    }
}
