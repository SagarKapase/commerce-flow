using Microsoft.EntityFrameworkCore;
using Payment.Api.Domain;
using Payment.Api.Exceptions;
using Payment.Api.Gateway;
using Payment.Api.Persistence;

namespace Payment.Api.Payments;

public interface IPaymentService
{
    /// <returns>The payment, or <c>null</c> if no payment has that id.</returns>
    Task<PaymentResponse?> GetAsync(Guid paymentId, CancellationToken cancellationToken);

    /// <summary>
    /// Charges an order. Safe to call more than once for the same order.
    /// </summary>
    Task<PaymentResponse> ChargeAsync(
        CreatePaymentRequest request,
        CancellationToken cancellationToken);
}

public sealed class PaymentService : IPaymentService
{
    private readonly PaymentDbContext _dbContext;
    private readonly IPaymentGateway _gateway;
    private readonly ILogger<PaymentService> _logger;

    public PaymentService(
        PaymentDbContext dbContext,
        IPaymentGateway gateway,
        ILogger<PaymentService> logger)
    {
        _dbContext = dbContext;
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<PaymentResponse?> GetAsync(Guid paymentId, CancellationToken cancellationToken)
    {
        var payment = await _dbContext.Payments
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == paymentId, cancellationToken);

        return payment is null ? null : ToResponse(payment);
    }

    public async Task<PaymentResponse> ChargeAsync(
        CreatePaymentRequest request,
        CancellationToken cancellationToken)
    {
        // --------------------------------------------------------------
        // STEP 1a. HAVE WE ALREADY SEEN THIS PAYMENT ID?
        //
        // The caller chose the id, which makes it an idempotency key: the
        // SAME request sent twice is recognisable as one operation. This is
        // the check that makes Ordering's retry after a lost response safe,
        // because Ordering still has the id it saved before it called us.
        //
        // If the id exists but names a different order, that is not a retry -
        // it is a caller bug, and returning the existing payment would report
        // somebody else's charge as this order's. Refuse instead.
        // --------------------------------------------------------------
        var sameId = await _dbContext.Payments
            .FirstOrDefaultAsync(payment => payment.Id == request.PaymentId, cancellationToken);

        if (sameId is not null)
        {
            if (sameId.OrderId != request.OrderId)
            {
                throw new PaymentIdAlreadyUsedException(
                    request.PaymentId, sameId.OrderId, request.OrderId);
            }

            _logger.LogInformation(
                "Payment {PaymentId} already exists with status {Status} - returning it unchanged",
                sameId.Id,
                sameId.Status);

            return ToResponse(sameId);
        }

        // --------------------------------------------------------------
        // STEP 1b. HAVE WE ALREADY CHARGED THIS ORDER, UNDER ANOTHER ID?
        //
        // This is idempotency by NATURAL KEY: the operation already has a
        // unique identifier that means something in the business - the order
        // id - so there is nothing to invent.
        //
        // Both checks are here because they answer different questions. 1a
        // catches "this exact request again"; 1b catches "a DIFFERENT attempt
        // to charge an order that is already charged", which is the case the
        // caller-chosen id cannot see. Keeping only 1a would let a fresh
        // attempt double-charge; keeping only 1b is what this service had
        // before, and it left Ordering unable to look its own payment up.
        //
        // Returning the existing payment rather than an error is deliberate.
        // The caller asked "charge this order"; the order is charged; that is
        // a success, whether or not this particular request caused it. A 409
        // would make a retry after a lost response look like a failure, which
        // is precisely backwards.
        //
        // Phase 13 will do the general case - an Idempotency-Key header for
        // operations with no natural key at all.
        // --------------------------------------------------------------
        var existing = await _dbContext.Payments
            .FirstOrDefaultAsync(payment => payment.OrderId == request.OrderId, cancellationToken);

        if (existing is not null)
        {
            _logger.LogInformation(
                "Order {OrderId} already has payment {PaymentId} with status {Status} - returning it unchanged",
                existing.OrderId,
                existing.Id,
                existing.Status);

            return ToResponse(existing);
        }

        // --------------------------------------------------------------
        // STEP 2. Record the attempt BEFORE calling the gateway.
        //
        // Same rule as Ordering in Phase 8: write your own durable record
        // first, then cause the external effect. If the process dies during
        // the charge, there is a Pending payment row saying "we were trying" -
        // which is something a reconciliation job can investigate against the
        // provider.
        //
        // The alternative - call the gateway, then save the result - can move
        // real money and leave no trace of it whatsoever. In a payment system
        // that is not an edge case, it is the thing you get audited for.
        // --------------------------------------------------------------
        var payment = OrderPayment.Create(
            request.PaymentId, request.OrderId, request.CustomerId, request.Amount);

        _dbContext.Payments.Add(payment);
        await _dbContext.SaveChangesAsync(cancellationToken);

        // --------------------------------------------------------------
        // STEP 3. Ask the provider.
        //
        // Note the cancellation token IS passed here, so a caller hanging up
        // stops us waiting - but see SimulatedPaymentGateway: the gateway
        // itself deliberately ignores it for tok_timeout, because a real
        // provider does not abandon a charge just because the client left.
        // --------------------------------------------------------------
        var result = await _gateway.ChargeAsync(
            request.PaymentMethodToken, payment.Amount, cancellationToken);

        // --------------------------------------------------------------
        // STEP 4. Record what happened.
        // --------------------------------------------------------------
        if (result.Succeeded)
        {
            payment.MarkSucceeded(result.ProviderReference!);

            _logger.LogInformation(
                "Payment {PaymentId} for order {OrderId} succeeded ({ProviderReference})",
                payment.Id,
                payment.OrderId,
                result.ProviderReference);
        }
        else
        {
            payment.MarkFailed(result.FailureReason!);

            _logger.LogWarning(
                "Payment {PaymentId} for order {OrderId} was declined: {Reason}",
                payment.Id,
                payment.OrderId,
                result.FailureReason);
        }

        // ==============================================================
        // CancellationToken.None - AND THIS IS NOT AN OVERSIGHT.
        //
        // This line was originally SaveChangesAsync(cancellationToken), and
        // running the tok_timeout path exposed the bug:
        //
        //   1. Ordering's HttpClient gave up after 5 seconds and disconnected.
        //   2. ASP.NET Core cancelled this request's token.
        //   3. The gateway finished anyway and approved the charge.
        //   4. This save was handed an already-cancelled token, threw
        //      OperationCanceledException, and wrote NOTHING.
        //
        // Result: the provider had taken the money and our own database still
        // said Pending. Our record of a real-world side effect was thrown away
        // because a client hung up - which is about as bad as a payment system
        // gets.
        //
        // THE PRINCIPLE: a cancellation token means "this work can be safely
        // abandoned". Recording that money HAS ALREADY MOVED is never that.
        // Anything after an irreversible external effect must complete
        // regardless of who is still listening.
        //
        // The first save (step 2) keeps its token on purpose - abandoning
        // BEFORE the charge is entirely safe, because nothing has happened yet.
        // ==============================================================
        await _dbContext.SaveChangesAsync(CancellationToken.None);

        return ToResponse(payment);
    }

    private static PaymentResponse ToResponse(OrderPayment payment)
    {
        return new PaymentResponse(
            payment.Id,
            payment.OrderId,
            payment.CustomerId,
            payment.Amount,
            payment.Status.ToString(),
            payment.ProviderReference,
            payment.FailureReason,
            payment.CreatedAtUtc,
            payment.CompletedAtUtc);
    }
}
