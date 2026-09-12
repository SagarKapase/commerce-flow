using Microsoft.Extensions.Logging;
using Ordering.Application.Abstractions;
using Ordering.Application.Common;
using Ordering.Application.Exceptions;
using Ordering.Application.Orders.Dtos;
using Ordering.Domain.Entities;
using Ordering.Domain.ValueObjects;

namespace Ordering.Application.Orders;

public sealed class OrderService : IOrderService
{
    private readonly IOrderRepository _orders;
    private readonly IBasketClient _basketClient;
    private readonly IInventoryClient _inventoryClient;
    private readonly IPaymentClient _paymentClient;
    private readonly IOrderEventPublisher _events;
    private readonly ILogger<OrderService> _logger;

    public OrderService(
        IOrderRepository orders,
        IBasketClient basketClient,
        IInventoryClient inventoryClient,
        IPaymentClient paymentClient,
        IOrderEventPublisher events,
        ILogger<OrderService> logger)
    {
        _orders = orders;
        _basketClient = basketClient;
        _inventoryClient = inventoryClient;
        _paymentClient = paymentClient;
        _events = events;
        _logger = logger;
    }

    /// <summary>
    /// ===================================================================
    /// THE ORCHESTRATION. Read the step order carefully - it is the design.
    ///
    ///   1. Read the customer's basket          (Basket, over HTTP)
    ///   2. Create the order and SAVE IT        (our own database)
    ///   3. Reserve the stock                   (Inventory, over HTTP)
    ///   4. Record the outcome and SAVE AGAIN   (our own database)
    ///   5. Empty the basket                    (Basket, best effort)
    ///
    /// Three services, three databases, and NO TRANSACTION SPANNING THEM.
    /// There is no `using var transaction = ...` that can cover an HTTP call to
    /// another process, and there never will be. Everything below is about
    /// making the failures survivable rather than preventing them.
    /// ===================================================================
    /// </summary>
    public async Task<OrderResponse> PlaceAsync(
        Guid customerId,
        CreateOrderRequest request,
        CancellationToken cancellationToken)
    {
        // -------------------------------------------------------------
        // STEP 1. What does the customer actually want to buy?
        //
        // No product ids or prices came in the request. They come from the
        // basket, which Basket priced from Catalog in Phase 5. Notice we do not
        // pass customerId - Basket reads the owner from the forwarded token, so
        // there is no way to ask for somebody else's basket even by mistake.
        //
        // If Basket is down this throws, and NOTHING has happened yet. Doing
        // the fragile thing first, while failing is still free, is the cheapest
        // reliability decision in this method.
        // -------------------------------------------------------------
        var basketLines = await _basketClient.GetCurrentBasketAsync(cancellationToken);

        if (basketLines.Count == 0)
        {
            throw new EmptyBasketException();
        }

        // -------------------------------------------------------------
        // STEP 2. Create the order and COMMIT IT before touching anything
        // external. This ordering is deliberate and worth defending.
        //
        // The tempting alternative is to reserve stock first and save the order
        // once, complete, at the end. It is one database round trip instead of
        // two - and it is wrong, because of what each version leaves behind
        // when the process dies at the worst moment:
        //
        //   Save last:  stock is reserved and NOTHING references the
        //               reservation. Units are held forever for an order that
        //               does not exist. Nobody knows to release them, because
        //               nobody knows they were taken.
        //
        //   Save first: an order sits in Pending with no reservation. Ugly, but
        //               VISIBLE - it is a row, in our own database, that a
        //               reaper or a retry can find and finish or expire.
        //
        // The principle generalises far past this method: WHEN YOU CANNOT HAVE
        // ATOMICITY, ORDER THE STEPS SO THE FAILURE MODE IS RECOVERABLE. Write
        // your own durable record first, then cause the external effect.
        //
        // It is also exactly the reasoning behind the Outbox in Phase 12, where
        // the "durable record first" becomes a row in an OutboxMessages table.
        // -------------------------------------------------------------
        var order = Order.Create(
            customerId,
            new Address(
                request.ShippingAddress.Line1.Trim(),
                request.ShippingAddress.City.Trim(),
                request.ShippingAddress.PostalCode.Trim(),
                request.ShippingAddress.Country.Trim()),
            basketLines
                .Select(line => (line.ProductId, line.ProductName, line.UnitPrice, line.Quantity))
                .ToList());

        _orders.Add(order);
        await _orders.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Order {OrderId} created as Pending for customer {CustomerId}: {Total} across {ItemCount} line(s)",
            order.Id,
            customerId,
            order.TotalAmount,
            order.Items.Count);

        // -------------------------------------------------------------
        // STEP 3. Ask Inventory to hold the stock.
        //
        // If this throws, the order stays Pending and the exception reaches the
        // caller as a 503. THE ORDER STILL EXISTS - go and look at it in
        // my-orders. That ghost row is not a bug in this code, it is the
        // honest cost of orchestrating across services without a transaction,
        // and Phases 11 and 12 exist to deal with it.
        //
        // Worse still, and worth saying out loud: if the request reached
        // Inventory and the RESPONSE was lost, stock is now held for an order
        // that will never progress. We cannot tell that case apart from "never
        // arrived" - which is why Phase 13 makes this call idempotent, so
        // retrying is safe.
        // -------------------------------------------------------------
        var reservationLines = order.Items
            .Select(item => new StockReservationLine(item.ProductId, item.Quantity))
            .ToList();

        var reservation = await _inventoryClient.ReserveAsync(
            order.Id, reservationLines, cancellationToken);

        // -------------------------------------------------------------
        // STEP 4. Record what Inventory decided.
        //
        // Both outcomes are normal, and both move the order to a state a
        // customer can understand. Nothing here throws: "we could not get you
        // the stock" is an ANSWER, and the customer is entitled to it in the
        // form of an order they can look at, with a reason attached.
        // -------------------------------------------------------------
        if (reservation.Reserved)
        {
            order.MarkInventoryReserved(reservation.ReservationId!.Value);
        }
        else
        {
            order.Reject(reservation.FailureReason!);

            _logger.LogWarning(
                "Order {OrderId} rejected: {Reason}",
                order.Id,
                reservation.FailureReason);
        }

        await _orders.SaveChangesAsync(cancellationToken);

        // -------------------------------------------------------------
        // STEP 4b. ANNOUNCE IT (Phase 11).
        //
        // AFTER the commit, never before. The event says "an order was
        // placed", and that must not be broadcast until it is actually true -
        // publishing first and then failing to save would tell the whole
        // system about an order that does not exist.
        //
        // This call cannot throw. See OrderEventPublisher for what that costs
        // and why it is still the right choice here.
        //
        // Note also what did NOT change: nothing below depends on the event.
        // The synchronous flow still does all the work. That is deliberate for
        // a first use of messaging - the consumer is additive, so a broken
        // broker degrades the system rather than breaking it. Phase 12 is when
        // the order flow starts actually relying on messages.
        // -------------------------------------------------------------
        await _events.PublishOrderPlacedAsync(order, cancellationToken);

        // -------------------------------------------------------------
        // STEP 5. Empty the basket - BEST EFFORT, and deliberately so.
        //
        // The order is placed and the stock is held. If clearing the basket
        // fails now, failing the whole request would be absurd: we would return
        // an error for an order that definitely exists, and the customer would
        // try again and buy everything twice.
        //
        // So we log and carry on. The visible consequence is a stale basket,
        // which is annoying and harmless - and note that Phase 10 fixes this
        // properly, by making "order placed" an EVENT that Basket reacts to on
        // its own schedule, retrying until it succeeds. That is the whole
        // argument for messaging in one paragraph: work that must happen but
        // need not happen NOW does not belong in the request.
        // -------------------------------------------------------------
        if (reservation.Reserved)
        {
            try
            {
                await _basketClient.ClearCurrentBasketAsync(cancellationToken);
            }
            catch (DownstreamUnavailableException exception)
            {
                _logger.LogWarning(
                    exception.InnerFailure ?? exception,
                    "Order {OrderId} was placed but the basket could not be cleared. " +
                    "The customer may see stale items",
                    order.Id);
            }
        }

        return OrderMappings.ToResponse(order);
    }

    public async Task<OrderResponse?> GetForCustomerAsync(
        Guid orderId,
        Guid customerId,
        bool callerIsAdmin,
        CancellationToken cancellationToken)
    {
        var order = await _orders.GetByIdAsync(orderId, cancellationToken);

        if (order is null || !CanBeSeenBy(order, customerId, callerIsAdmin))
        {
            // ONE return for two different situations, on purpose.
            //
            // "No such order" and "not your order" both come back as null, and
            // the controller turns both into 404. If the second returned 403,
            // an attacker could walk order ids and learn exactly which ones
            // exist from the difference between the two responses - a
            // side-channel that leaks how many orders the shop has taken and
            // lets somebody probe for a specific one.
            //
            // 404 for both means the response says nothing it does not have to.
            return null;
        }

        return OrderMappings.ToResponse(order);
    }

    public async Task<IReadOnlyList<OrderResponse>> GetMyOrdersAsync(
        Guid customerId,
        CancellationToken cancellationToken)
    {
        var orders = await _orders.GetByCustomerAsync(customerId, cancellationToken);

        return orders
            .Select(OrderMappings.ToResponse)
            .ToList();
    }

    public async Task<PagedResponse<OrderResponse>> GetPageAsync(
        OrderListQuery query,
        CancellationToken cancellationToken)
    {
        var page = Math.Max(query.Page, 1);
        var pageSize = Math.Clamp(query.PageSize, 1, OrderListQuery.MaxPageSize);

        var (orders, totalCount) = await _orders.GetPageAsync(
            page, pageSize, query.Status, cancellationToken);

        return new PagedResponse<OrderResponse>(
            orders.Select(OrderMappings.ToResponse).ToList(),
            page,
            pageSize,
            totalCount);
    }


    /// <summary>
    /// ===================================================================
    /// THE MANUAL SAGA.
    ///
    /// This is Phase 11's distributed workflow written out by hand, in one
    /// method, synchronously - so that when we replace it with a real saga you
    /// already know exactly what the machinery is doing and why.
    ///
    ///   HAPPY PATH
    ///     PaymentProcessing -> charge -> confirm reservation -> Confirmed
    ///
    ///   COMPENSATION
    ///     PaymentProcessing -> declined -> RELEASE reservation -> PaymentFailed
    ///
    /// The release is the whole point. Three databases, no ROLLBACK between
    /// them, so undoing the stock hold is not a rollback at all - it is a
    /// second business operation that reverses the first, and it leaves a
    /// record of both having happened.
    /// ===================================================================
    /// </summary>
    public async Task<OrderResponse?> PayAsync(
        Guid orderId,
        Guid customerId,
        bool callerIsAdmin,
        PayOrderRequest request,
        CancellationToken cancellationToken)
    {
        var order = await _orders.GetByIdAsync(orderId, cancellationToken);

        if (order is null || !CanBeSeenBy(order, customerId, callerIsAdmin))
        {
            return null;
        }

        // -------------------------------------------------------------
        // STEP 1. Move to PaymentProcessing and SAVE, before any money moves.
        //
        // Order.StartPayment refuses unless the order is InventoryReserved, so
        // paying twice, or paying an order with no stock held, is impossible -
        // the aggregate written in Phase 7 is doing the work here, with no
        // extra checks in this method.
        //
        // Saving first matters for the same reason it did in Phase 8: if this
        // process dies mid-charge, the order says PaymentProcessing, which is
        // a visible state a human or a recovery job can investigate. Saving
        // afterwards would mean the money moved and the order still said
        // InventoryReserved - so a retry would charge the card again.
        //
        // The payment id is not known yet, so we mint it here and pass the
        // SAME id to Payment. That is deliberate, for two reasons: the caller
        // choosing the id is what lets a retry be recognised as the same
        // payment rather than a second one, and it means this id is saved on
        // the order BEFORE the risky call - so an order left in
        // PaymentProcessing can be asked about with GET /api/payments/{id}
        // even though the reply that would have carried the id never arrived.
        //
        // Saving an id that Payment then invents for itself would be worse
        // than useless: it would look like a working reference and point at
        // nothing.
        // -------------------------------------------------------------
        var paymentId = Guid.CreateVersion7();

        order.StartPayment(paymentId);
        await _orders.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Order {OrderId} moved to PaymentProcessing for {Amount}",
            order.Id,
            order.TotalAmount);

        // -------------------------------------------------------------
        // STEP 2. Charge the card.
        //
        // If this THROWS, the order is left in PaymentProcessing and the
        // exception becomes a 503. That state is a trap and we know it: the
        // charge may have succeeded. Nothing here can safely guess, which is
        // why the automatic recovery arrives in Phase 11 and the safe retry in
        // Phase 13.
        // -------------------------------------------------------------
        var payment = await _paymentClient.ChargeAsync(
            paymentId,
            order.Id,
            order.CustomerId,
            order.TotalAmount,
            request.PaymentMethodToken,
            cancellationToken);

        if (payment.Succeeded)
        {
            // ---------------------------------------------------------
            // STEP 3a. Paid. Turn the hold into a sale.
            //
            // Confirm BEFORE marking the order Confirmed. If confirming the
            // reservation fails, we throw with the order still saying
            // PaymentProcessing - which is recoverable, because the money is
            // known to have moved and a retry of Confirm is idempotent.
            //
            // The other order would mark the order Confirmed and then fail to
            // consume the stock, leaving units reserved forever for an order
            // everybody believes is finished. Once again: sequence the steps so
            // the failure is the recoverable one.
            // ---------------------------------------------------------
            await _inventoryClient.ConfirmAsync(
                order.InventoryReservationId!.Value, cancellationToken);

            order.Confirm();

            await _orders.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Order {OrderId} confirmed against payment {PaymentId}",
                order.Id,
                payment.PaymentId);

            await _events.PublishOrderConfirmedAsync(order, cancellationToken);
        }
        else
        {
            // ---------------------------------------------------------
            // STEP 3b. Declined. COMPENSATE.
            //
            // The stock has been held since the order was placed. Nobody paid
            // for it, so it goes back on the shelf for the next customer -
            // and if we forgot this line, every declined card would silently
            // consume inventory until the shop appeared sold out of everything.
            //
            // Release is idempotent (Phase 6), so a retry after a failure here
            // is safe. That is not luck - it is why it was written that way.
            // ---------------------------------------------------------
            await _inventoryClient.ReleaseAsync(
                order.InventoryReservationId!.Value, cancellationToken);

            order.MarkPaymentFailed(payment.FailureReason!);

            await _orders.SaveChangesAsync(cancellationToken);

            _logger.LogWarning(
                "Order {OrderId} failed payment ({Reason}); stock released",
                order.Id,
                payment.FailureReason);

            await _events.PublishOrderPaymentFailedAsync(order, cancellationToken);
        }

        return OrderMappings.ToResponse(order);
    }

    public async Task<OrderResponse?> CancelAsync(
        Guid orderId,
        Guid customerId,
        bool callerIsAdmin,
        CancellationToken cancellationToken)
    {
        var order = await _orders.GetByIdAsync(orderId, cancellationToken);

        if (order is null || !CanBeSeenBy(order, customerId, callerIsAdmin))
        {
            return null;
        }

        // The service does not check the status first. It asks the aggregate to
        // cancel and lets it refuse, because the rule about which states allow
        // cancellation belongs to the Order and nowhere else. Duplicating that
        // check here would be a second copy to keep in step - and the copy
        // outside the aggregate is always the one that goes stale.
        order.Cancel();

        await _orders.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Order {OrderId} cancelled by {Actor}",
            order.Id,
            callerIsAdmin ? "an administrator" : "the customer");

        return OrderMappings.ToResponse(order);
    }

    /// <summary>
    /// An order is visible to the customer who placed it, and to administrators.
    /// </summary>
    private static bool CanBeSeenBy(Order order, Guid customerId, bool callerIsAdmin)
    {
        return callerIsAdmin || order.CustomerId == customerId;
    }
}
