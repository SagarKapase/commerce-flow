namespace Ordering.Application.Exceptions;

/// <summary>Base type for expected failures raised by the application layer.</summary>
public abstract class OrderingApplicationException : Exception
{
    protected OrderingApplicationException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// Thrown when a customer tries to place an order with an empty basket.
/// Maps to 400 Bad Request.
///
/// Before Phase 8 this could not happen, because the items came in the request
/// body. Now they come from the basket, so "you have not chosen anything yet"
/// becomes a real answer the API has to give.
/// </summary>
public sealed class EmptyBasketException : OrderingApplicationException
{
    public EmptyBasketException()
        : base("Your basket is empty. Add something to it before placing an order.")
    {
    }
}

/// <summary>
/// Thrown when a service Ordering depends on could not be reached, timed out,
/// or answered with something unusable. Maps to 503 Service Unavailable.
///
/// The service name is carried separately so the LOG can say which dependency
/// failed while the RESPONSE stays vague. A customer does not need to know our
/// internal topology; the engineer on call needs to know it precisely.
/// </summary>
public sealed class DownstreamUnavailableException : OrderingApplicationException
{
    public DownstreamUnavailableException(string serviceName, string message, Exception? innerFailure = null)
        : base(message)
    {
        ServiceName = serviceName;
        InnerFailure = innerFailure;
    }

    public string ServiceName { get; }

    /// <summary>Kept for the log, never for the response.</summary>
    public Exception? InnerFailure { get; }
}

/// <summary>
/// Thrown when the payment service refuses the request itself - an
/// unrecognised payment method token, for instance. Maps to 400 Bad Request.
///
/// NOT the same as a declined card. A decline is a normal business outcome
/// that produces a PaymentFailed order the customer can look at; this means
/// the request could not be processed at all, so no order state changes.
/// </summary>
public sealed class PaymentRejectedException : OrderingApplicationException
{
    public PaymentRejectedException(string message)
        : base(message)
    {
    }
}
