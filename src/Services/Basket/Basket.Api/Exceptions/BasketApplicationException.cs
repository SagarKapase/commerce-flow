namespace Basket.Api.Exceptions;

/// <summary>
/// Base type for "the request was well-formed, but a basket rule says no".
/// Third service, third copy of this idea - Catalog and Identity have the same
/// pattern, and the exception handler uses it to decide Warning versus Error.
/// </summary>
public abstract class BasketApplicationException : Exception
{
    protected BasketApplicationException(string message)
        : base(message)
    {
    }
}
