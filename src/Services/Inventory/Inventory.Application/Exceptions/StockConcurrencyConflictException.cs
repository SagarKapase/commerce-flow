namespace Inventory.Application.Exceptions;

/// <summary>
/// Thrown when somebody else changed the same stock row between our read and
/// our write. Maps to 409 Conflict.
///
/// This is the exception the whole phase builds towards, so it is worth being
/// precise about what happened:
///
///   1. Request A reads product X: Available = 1, Version = 7.
///   2. Request B reads the same row: Available = 1, Version = 7.
///   3. Both pass the "is there enough?" check. Both are correct - at the
///      moment each of them looked, there was.
///   4. A saves. EF issues
///          UPDATE InventoryItems SET Available = 0, Version = 8
///          WHERE ProductId = X AND Version = 7
///      One row updated. A wins.
///   5. B saves the same statement. Version is now 8, so the WHERE matches
///      NOTHING. Zero rows affected. EF notices the count is not what it
///      expected and throws DbUpdateConcurrencyException.
///
/// Without the version check, step 5 would succeed and overwrite A - the
/// classic LOST UPDATE. Both customers get told they bought the last unit, and
/// the shop finds out at packing time.
///
/// The right answer for the loser is 409, not 500: nothing is broken, they
/// simply raced and lost. Retrying is a sensible thing for the client to do,
/// and on the retry they will read Available = 0 and get an honest "out of
/// stock" instead.
/// </summary>
public sealed class StockConcurrencyConflictException : InventoryApplicationException
{
    public StockConcurrencyConflictException()
        : base("The stock for one of these products changed while this request was being processed. Please try again.")
    {
    }
}
