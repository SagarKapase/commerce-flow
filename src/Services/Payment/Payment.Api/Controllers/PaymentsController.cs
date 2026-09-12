using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Payment.Api.Payments;

namespace Payment.Api.Controllers;

/// <summary>
/// Charging orders.
///
/// ============ THIS IS AN INTERNAL API ============
/// Every endpoint here is [Authorize] with no role - the same placeholder
/// Inventory's reservation endpoints carry, and for the same unresolved
/// reason. It is looser than it should be, and saying so is better than
/// letting it become "the way it has always been".
///
/// The gap is real and specific: this service takes the AMOUNT from its
/// caller, because it cannot verify it against an order without calling
/// Ordering - which is what called us. So a customer who could reach
/// POST /api/payments directly could pay one paisa for a laptop.
///
/// Three ways to close it, in increasing order of correctness:
///
///   1. Do not expose it. Phase 10's API Gateway will route /api/catalog,
///      /api/basket and /api/orders and will simply have no route to
///      /api/payments or /api/inventory/reservations. Internal services stay
///      internal because nothing outside can address them. This is how most
///      real systems actually do it, and it is the least code.
///   2. Service credentials. Payment requires a token issued to the "ordering"
///      client rather than to a person, so a customer token is rejected here
///      whatever the network allows.
///   3. Both. Defence in depth - the network keeps honest callers out, the
///      credential keeps the dishonest ones out too.
///
/// Phase 10 does (1). Naming (2) here so it is a decision rather than an
/// oversight.
/// =================================================
/// </summary>
[ApiController]
[Route("api/payments")]
[Produces("application/json")]
[Authorize]
public sealed class PaymentsController : ControllerBase
{
    private readonly IPaymentService _paymentService;

    public PaymentsController(IPaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    /// <summary>Charges an order.</summary>
    /// <remarks>
    /// POST /api/payments
    /// Body: CreatePaymentRequest
    /// Responses:
    ///   201 Created - the payment record. READ ITS status: "Succeeded" or
    ///                 "Failed". A declined card is a successful REQUEST that
    ///                 produced a negative business outcome.
    ///   400 Bad Request - unknown payment method token
    ///   401 Unauthorized
    ///
    /// Breakpoints: PaymentsController.Charge -> PaymentService.ChargeAsync
    ///              -> SimulatedPaymentGateway.ChargeAsync
    /// Watch: existing (null on the first call, populated on a retry),
    ///        payment.Status before and after the gateway answers
    ///
    /// SAFE TO CALL TWICE. A second charge for the same order returns the
    /// first payment unchanged, because the order id is a natural idempotency
    /// key and there is a unique index behind it. Double-charging is the one
    /// bug a payment system is not allowed to have.
    ///
    /// A DECLINE IS NOT AN HTTP ERROR. The request was well-formed, we were
    /// allowed to make it, and the system worked perfectly - the bank said no.
    /// Returning 402 or 409 would conflate "your request was wrong" with
    /// "your card was refused", and would hide the fact that a payment RECORD
    /// now exists that the customer and a finance team can both look at.
    /// </remarks>
    [HttpPost]
    [ProducesResponseType(typeof(PaymentResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<PaymentResponse>> Charge(
        CreatePaymentRequest request,
        CancellationToken cancellationToken)
    {
        var payment = await _paymentService.ChargeAsync(request, cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = payment.Id }, payment);
    }

    /// <summary>Gets one payment.</summary>
    /// <remarks>
    /// GET /api/payments/{id}
    /// Responses: 200 OK, 401 Unauthorized, 404 Not Found
    /// </remarks>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(PaymentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PaymentResponse>> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var payment = await _paymentService.GetAsync(id, cancellationToken);

        if (payment is null)
        {
            return NotFound();
        }

        return Ok(payment);
    }
}
