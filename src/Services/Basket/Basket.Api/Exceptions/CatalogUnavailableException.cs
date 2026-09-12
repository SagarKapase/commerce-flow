namespace Basket.Api.Exceptions;

/// <summary>
/// Thrown when Basket could not get an answer out of Catalog at all - it was
/// unreachable, it timed out, or it returned something we cannot use.
///
/// Maps to 503 Service Unavailable, and the choice of code is the lesson:
///
///   500 would be a LIE. It says "this service has a bug". Basket does not.
///        It would also send whoever is on call hunting through the wrong
///        codebase at three in the morning.
///   502  would be closer - Basket is acting as a gateway here - but Basket is
///        not a proxy, it is a service that happens to need another one.
///   503  says exactly what happened: this request could not be served RIGHT
///        NOW, for reasons that are temporary. It is also the only one of the
///        three that tells a client "retrying later is sensible", which is true
///        and actionable.
///
/// It derives from BasketApplicationException so the message is safe to return
/// to the caller - there is nothing sensitive in "the catalog service could not
/// be reached". The exception handler still logs it at Error rather than
/// Warning, because a dependency being down is not business-as-usual even
/// though it is not our bug.
/// </summary>
public sealed class CatalogUnavailableException : BasketApplicationException
{
    public CatalogUnavailableException(string message)
        : base(message)
    {
    }

    public CatalogUnavailableException(string message, Exception innerException)
        : base(message)
    {
        // The inner exception is kept for the log, never for the response.
        // A customer does not need our socket errors; the on-call engineer does.
        InnerFailure = innerException;
    }

    public Exception? InnerFailure { get; }
}
