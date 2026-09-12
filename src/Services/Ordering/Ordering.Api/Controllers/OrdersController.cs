using CommerceFlow.BuildingBlocks.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ordering.Api.Authentication;
using Ordering.Application.Common;
using Ordering.Application.Orders;
using Ordering.Application.Orders.Dtos;

namespace Ordering.Api.Controllers;

/// <summary>
/// Orders.
///
/// ============ THERE IS NO PUT IN THIS FILE, AND THAT IS THE POINT ============
///
/// Every other resource in CommerceFlow has one. A product can be edited; a
/// basket line's quantity can be replaced. An order cannot, because an order is
/// not a document - it is a RECORD OF A COMMITMENT, and commitments are not
/// edited, they are made and then acted upon.
///
/// A `PUT /api/orders/{id}` accepting the whole order would let a caller set
/// Status to "Confirmed" on an order nobody paid for, change the price after
/// the fact, or add an item that was never reserved. Every one of those is
/// unreachable here, not because a check catches it, but because no route
/// exists that could express it.
///
/// So the API is verbs the business would recognise:
///     POST /api/orders              place an order
///     POST /api/orders/{id}/cancel  call it off
///
/// The internal steps - inventory reserved, payment started, order confirmed -
/// have NO endpoints at all. They are transitions the system makes about
/// itself, driven by Phase 8's orchestration and Phase 11's saga. Exposing them
/// over HTTP would mean anybody with a token could tell us their payment
/// succeeded.
///
/// "CRUD does not mean blind CRUD": expose the operations that make business
/// sense, not the four that happen to map to SQL statements.
/// ============================================================================
/// </summary>
[ApiController]
[Route("api/orders")]
[Produces("application/json")]
[Authorize]
public sealed class OrdersController : ControllerBase
{
    private readonly IOrderService _orderService;

    public OrdersController(IOrderService orderService)
    {
        _orderService = orderService;
    }

    /// <summary>Places an order.</summary>
    /// <remarks>
    /// POST /api/orders
    /// Body: CreateOrderRequest
    /// Responses:
    ///   201 Created - Location points at GET /api/orders/{id}. Status: Pending.
    ///   400 Bad Request - validation failed, or duplicate products in the body
    ///   401 Unauthorized
    /// Authorization: any authenticated user
    ///
    /// Breakpoints: OrdersController.Place -> OrderService.PlaceAsync
    ///              -> Order.Create
    /// Watch: customerId (from the token, NOT the body), order.Items,
    ///        order.TotalAmount, order.Status
    ///
    /// The customer id comes from the token. There is no field in
    /// CreateOrderRequest that could name a different one - the same design
    /// rule as Basket, for the same reason.
    /// </remarks>
    [HttpPost]
    [ProducesResponseType(typeof(OrderResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<OrderResponse>> Place(
        CreateOrderRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var customerId))
        {
            return Unauthorized();
        }

        var order = await _orderService.PlaceAsync(customerId, request, cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = order.Id }, order);
    }

    /// <summary>Gets one order.</summary>
    /// <remarks>
    /// GET /api/orders/{id}
    /// Responses: 200 OK, 401 Unauthorized, 404 Not Found
    /// Authorization: the customer who placed it, or an Admin
    ///
    /// An order belonging to somebody else returns 404, NOT 403. A 403 would
    /// confirm the order exists, which lets an attacker walk ids and count the
    /// shop's orders. 404 says nothing it does not have to.
    /// </remarks>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(OrderResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OrderResponse>> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var customerId))
        {
            return Unauthorized();
        }

        var order = await _orderService.GetForCustomerAsync(
            id, customerId, IsAdmin(), cancellationToken);

        if (order is null)
        {
            return NotFound();
        }

        return Ok(order);
    }

    /// <summary>The signed-in customer's order history, newest first.</summary>
    /// <remarks>
    /// GET /api/orders/my-orders
    /// Responses: 200 OK (possibly empty), 401 Unauthorized
    /// Authorization: any authenticated user
    ///
    /// A literal route segment, so it must be declared before nothing else
    /// conflicts with it - "my-orders" is not a Guid, so the {id:guid}
    /// constraint on GetById means the two can never be confused. Without that
    /// constraint, /api/orders/my-orders would try to bind "my-orders" as an id.
    /// Route constraints are not just validation; they are disambiguation.
    /// </remarks>
    [HttpGet("my-orders")]
    [ProducesResponseType(typeof(IReadOnlyList<OrderResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<OrderResponse>>> GetMyOrders(
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var customerId))
        {
            return Unauthorized();
        }

        var orders = await _orderService.GetMyOrdersAsync(customerId, cancellationToken);

        return Ok(orders);
    }

    /// <summary>Every order, paged. Administrators only.</summary>
    /// <remarks>
    /// GET /api/orders?status=Pending&amp;page=1&amp;pageSize=20
    /// Responses: 200 OK, 400 Bad Request, 401, 403 Forbidden
    /// Authorization: Admin
    ///
    /// Here a role IS the right mechanism: "may you see other people's orders"
    /// is a question about what kind of user you are, not about which rows are
    /// yours. Contrast with GetById, where ownership does the work.
    /// </remarks>
    [HttpGet]
    [Authorize(Roles = OrderingRoles.Admin)]
    [ProducesResponseType(typeof(PagedResponse<OrderResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagedResponse<OrderResponse>>> GetAll(
        [FromQuery] OrderListQuery query,
        CancellationToken cancellationToken)
    {
        var orders = await _orderService.GetPageAsync(query, cancellationToken);

        return Ok(orders);
    }


    /// <summary>Pays for an order.</summary>
    /// <remarks>
    /// POST /api/orders/{id}/pay
    /// Body: PayOrderRequest  { "paymentMethodToken": "tok_success" }
    /// Responses:
    ///   200 OK - the updated order. READ ITS status: "Confirmed" or
    ///            "PaymentFailed". A declined card is a successful REQUEST.
    ///   400 Bad Request - the payment method was not accepted at all
    ///   401 Unauthorized
    ///   404 Not Found - no such order, or not yours
    ///   409 Conflict - the order is not in a state where paying makes sense
    ///   503 Service Unavailable - Payment or Inventory could not be reached
    /// Authorization: the customer who placed it, or an Admin
    ///
    /// Test tokens: tok_success, tok_declined, tok_insufficient_funds,
    ///              tok_timeout.
    ///
    /// WHY PAYING IS A SEPARATE CALL FROM PLACING THE ORDER:
    /// It mirrors what a customer actually does - review the order, then pay -
    /// and it keeps two failure surfaces apart. Placing can fail because stock
    /// ran out; paying can fail because a card was declined. Folding them into
    /// one endpoint would mean one request that can fail for six reasons, and
    /// a client that cannot tell which of them happened.
    ///
    /// Breakpoints: OrdersController.Pay -> OrderService.PayAsync
    ///              -> Order.StartPayment -> PaymentClient.ChargeAsync
    ///              -> InventoryClient.ConfirmAsync / ReleaseAsync
    /// Watch: order.Status at every step, order.PaymentId,
    ///        order.InventoryReservationId
    /// </remarks>
    [HttpPost("{id:guid}/pay")]
    [ProducesResponseType(typeof(OrderResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<OrderResponse>> Pay(
        Guid id,
        PayOrderRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var customerId))
        {
            return Unauthorized();
        }

        var order = await _orderService.PayAsync(
            id, customerId, IsAdmin(), request, cancellationToken);

        if (order is null)
        {
            return NotFound();
        }

        return Ok(order);
    }

    /// <summary>Cancels an order.</summary>
    /// <remarks>
    /// POST /api/orders/{id}/cancel
    /// Responses:
    ///   200 OK - the updated order
    ///   401 Unauthorized
    ///   404 Not Found - no such order, or not yours
    ///   409 Conflict - the order is past the point where cancelling is allowed
    /// Authorization: the customer who placed it, or an Admin
    ///
    /// POST on a sub-resource, not PUT on a status field. The URL names what
    /// the customer wants to DO. It also means the server decides whether that
    /// is currently possible - Order.Cancel refuses from PaymentProcessing,
    /// Confirmed and the terminal states, and no request shape can talk it out
    /// of that.
    ///
    /// Breakpoint: OrdersController.Cancel -> OrderService.CancelAsync
    ///             -> Order.Cancel  (watch Status before and after)
    /// </remarks>
    [HttpPost("{id:guid}/cancel")]
    [ProducesResponseType(typeof(OrderResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<OrderResponse>> Cancel(
        Guid id,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var customerId))
        {
            return Unauthorized();
        }

        var order = await _orderService.CancelAsync(
            id, customerId, IsAdmin(), cancellationToken);

        if (order is null)
        {
            return NotFound();
        }

        return Ok(order);
    }

    /// <summary>
    /// The caller's id, from the validated token. Never from the request.
    /// </summary>
    private bool TryGetUserId(out Guid userId)
    {
        var subject = User.FindFirst(JwtClaimNames.Sub)?.Value;

        return Guid.TryParse(subject, out userId);
    }

    /// <summary>
    /// Reads the role from the token's claims - no database lookup, and no call
    /// to Identity. Ordering has no users table at all.
    /// </summary>
    private bool IsAdmin() => User.IsInRole(OrderingRoles.Admin);
}
