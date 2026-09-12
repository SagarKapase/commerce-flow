namespace Inventory.Domain.Entities;

/// <summary>
/// The life of a reservation. Three states, and only two legal transitions.
///
///        Held ──confirm──> Confirmed   (goods shipped, stock gone for good)
///          │
///          └──release────> Released    (order failed, stock returned)
///
/// Confirmed and Released are both TERMINAL. There is no path back, and no
/// path between them: you cannot un-ship goods, and you cannot ship goods you
/// have already put back on the shelf.
///
/// Drawing the legal transitions before writing the code is the habit worth
/// taking from this file. Every state machine bug is a transition somebody
/// never decided about.
/// </summary>
public enum ReservationStatus
{
    /// <summary>Units are set aside, waiting on payment.</summary>
    Held = 1,

    /// <summary>Payment succeeded; the units have left inventory permanently.</summary>
    Confirmed = 2,

    /// <summary>The order failed or was cancelled; units went back to Available.</summary>
    Released = 3
}
