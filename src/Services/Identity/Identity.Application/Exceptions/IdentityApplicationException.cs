namespace Identity.Application.Exceptions;

/// <summary>
/// Base type for expected business failures in the Identity service.
/// Same idea as CatalogApplicationException: it separates "the system worked
/// and said no" from "the system broke", so the two get different log levels
/// and different amounts of detail in the response.
/// </summary>
public abstract class IdentityApplicationException : Exception
{
    protected IdentityApplicationException(string message)
        : base(message)
    {
    }
}
