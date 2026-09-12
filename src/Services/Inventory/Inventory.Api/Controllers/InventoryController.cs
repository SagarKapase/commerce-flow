using Inventory.Api.Authentication;
using Inventory.Application.Stock;
using Inventory.Application.Stock.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Api.Controllers;

/// <summary>
/// Stock levels and stock corrections.
///
/// Split from InventoryReservationsController on purpose. These two controllers
/// have completely different audiences: this one is for warehouse operators
/// looking at and correcting counts, the other is machinery the Ordering
/// service drives. One controller holding both would need two authorization
/// stories and would read like two files anyway.
/// </summary>
[ApiController]
[Route("api/inventory")]
[Produces("application/json")]
public sealed class InventoryController : ControllerBase
{
    private readonly IStockService _stockService;

    public InventoryController(IStockService stockService)
    {
        _stockService = stockService;
    }

    /// <summary>Gets the stock position for one product.</summary>
    /// <remarks>
    /// GET /api/inventory/{productId}
    /// Responses: 200 OK, 401 Unauthorized, 404 Not Found
    /// Authorization: any authenticated user
    ///
    /// Not Admin-only: a storefront wants to show "3 left" to a customer.
    /// 404 means this product has never been counted, which is different from
    /// "we have none" - a product with zero stock returns 200 and a zero.
    /// </remarks>
    [HttpGet("{productId:guid}")]
    [Authorize]
    [ProducesResponseType(typeof(InventoryItemResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<InventoryItemResponse>> GetByProduct(
        Guid productId,
        CancellationToken cancellationToken)
    {
        var item = await _stockService.GetAsync(productId, cancellationToken);

        if (item is null)
        {
            return NotFound();
        }

        return Ok(item);
    }

    /// <summary>Receives or writes off stock.</summary>
    /// <remarks>
    /// POST /api/inventory/adjustments
    /// Body: StockAdjustmentRequest
    /// Responses: 200 OK, 400 Bad Request, 401, 403 Forbidden,
    ///            409 Conflict (would go negative, or lost a concurrency race)
    /// Authorization: Admin
    ///
    /// Breakpoints: InventoryController.Adjust -> StockService.AdjustAsync
    ///              -> InventoryItem.Adjust
    /// Watch: item.AvailableQuantity, item.ReservedQuantity, item.Version
    ///
    /// The URL is a noun - "adjustments" - and the verb is POST, because an
    /// adjustment is a THING that happened, not a field being edited. A
    /// PUT /api/inventory/{productId} that set AvailableQuantity directly would
    /// let a careless caller overwrite a number somebody else just changed, and
    /// would lose the reason entirely.
    /// </remarks>
    [HttpPost("adjustments")]
    [Authorize(Roles = InventoryRoles.Admin)]
    [ProducesResponseType(typeof(InventoryItemResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<InventoryItemResponse>> Adjust(
        StockAdjustmentRequest request,
        CancellationToken cancellationToken)
    {
        var item = await _stockService.AdjustAsync(request, cancellationToken);

        return Ok(item);
    }
}
