namespace Basket.Api.Exceptions;

/// <summary>
/// Thrown when adding an item would push the basket past a limit - too many
/// distinct products, or too many units on one line. Maps to 409 Conflict:
/// the request is valid, it just cannot be honoured given what is already in
/// the basket.
///
/// Note it is NOT 400. A 400 would say "your request is malformed", and it is
/// not - the exact same request would succeed against an emptier basket.
/// </summary>
public sealed class BasketLimitExceededException : BasketApplicationException
{
    public BasketLimitExceededException(string message)
        : base(message)
    {
    }
}
