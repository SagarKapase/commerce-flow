namespace Catalog.Application.Exceptions;

/// <summary>
/// Base type for "the request was well-formed, but a business rule says no".
///
/// The distinction matters at the edge of the system: anything deriving from
/// this is an EXPECTED outcome (log it as a Warning, tell the client exactly
/// what happened), while any other exception is a bug (log it as an Error,
/// return a generic 500 and never leak internals).
///
/// Note what is deliberately absent: an HTTP status code. The application layer
/// does not know it is being called over HTTP. Mapping these types to 400/409
/// happens in Catalog.Api/ErrorHandling/GlobalExceptionHandler.cs.
/// </summary>
public abstract class CatalogApplicationException : Exception
{
    protected CatalogApplicationException(string message)
        : base(message)
    {
    }
}
