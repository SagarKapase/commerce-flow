using CommerceFlow.BuildingBlocks.Authentication;
using Basket.Api.Baskets;
using Basket.Api.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Basket.Api.Controllers;

/// <summary>
/// The signed-in user's basket.
///
/// ==================================================================
/// THE ONE RULE THAT MATTERS IN THIS FILE
///
/// There is no user id in any route, any query string or any request body.
/// Every action calls GetUserId(), which reads the "sub" claim out of the
/// token the framework already validated. A caller cannot ask for somebody
/// else's basket, because there is nowhere to say whose basket they want.
///
/// The alternative - GET /api/basket/{userId} - looks harmless and is the
/// single most common serious API vulnerability there is. OWASP calls it
/// Broken Object Level Authorization and has ranked it #1 in the API Security
/// Top 10 since the list existed. It fails because the check ("is this your
/// id?") is something a developer has to remember to write, and eventually
/// somebody does not.
///
/// Designing the id out of the request means there is no check to forget.
/// ==================================================================
///
/// [Authorize] with no roles: any authenticated user. Basket makes no role
/// distinction - an admin's basket is just their basket.
/// </summary>
[ApiController]
[Route("api/basket")]
[Produces("application/json")]
[Authorize]
public sealed class BasketController : ControllerBase
{
    private readonly IBasketService _basketService;

    public BasketController(IBasketService basketService)
    {
        _basketService = basketService;
    }

    /// <summary>Gets the signed-in user's basket.</summary>
    /// <remarks>
    /// GET /api/basket
    /// Responses: 200 OK (empty basket if nothing has been added), 401 Unauthorized
    /// Authorization: any authenticated user
    /// Breakpoint: BasketController.Get -> BasketService.GetAsync
    /// Watch: User.Claims, userId
    ///
    /// Never 404. Every signed-in user conceptually has a basket; it may be
    /// empty. Making the client treat 404 as "empty" invites a null-reference
    /// bug on somebody's very first visit.
    /// </remarks>
    [HttpGet]
    [ProducesResponseType(typeof(BasketResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<BasketResponse>> Get(CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var basket = await _basketService.GetAsync(userId, cancellationToken);

        return Ok(basket);
    }

    /// <summary>Adds a product, or increases its quantity if already present.</summary>
    /// <remarks>
    /// POST /api/basket/items
    /// Body: AddBasketItemRequest
    /// Responses: 200 OK (the whole updated basket), 400 Bad Request,
    ///            401 Unauthorized, 409 Conflict (basket limits)
    /// Authorization: any authenticated user
    /// Breakpoint: BasketController.AddItem -> BasketService.AddItemAsync
    ///             -> CustomerBasket.AddItem
    ///
    /// WHY 200 AND NOT 201: a basket is a single document, not a collection of
    /// separately addressable resources. Returning the whole basket after every
    /// mutation means the client never needs a follow-up GET and can never
    /// render a total that disagrees with the server. The 201 + Location
    /// alternative would be defensible if basket lines were things you linked
    /// to; they are not.
    /// </remarks>
    [HttpPost("items")]
    [ProducesResponseType(typeof(BasketResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BasketResponse>> AddItem(
        AddBasketItemRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var basket = await _basketService.AddItemAsync(userId, request, cancellationToken);

        return Ok(basket);
    }

    /// <summary>Sets an existing line to an exact quantity.</summary>
    /// <remarks>
    /// PUT /api/basket/items/{productId}
    /// Responses: 200 OK, 400 Bad Request, 401 Unauthorized,
    ///            404 Not Found (that product is not in your basket)
    /// Authorization: any authenticated user
    ///
    /// PUT, not PATCH: the request replaces the quantity with an exact value
    /// rather than describing a change to it. That makes it idempotent - send
    /// "quantity 3" ten times and the basket holds three, whereas an "add 3"
    /// endpoint sent ten times holds thirty. Idempotency is what lets a client
    /// safely retry after a timeout.
    /// </remarks>
    [HttpPut("items/{productId:guid}")]
    [ProducesResponseType(typeof(BasketResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BasketResponse>> UpdateItemQuantity(
        Guid productId,
        UpdateBasketItemQuantityRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var basket = await _basketService.UpdateItemQuantityAsync(
            userId, productId, request.Quantity, cancellationToken);

        if (basket is null)
        {
            // The product is not in THIS user's basket. Note that a product
            // sitting in somebody else's basket is equally a 404 here - which
            // is correct, and also leaks nothing about other users.
            return NotFound();
        }

        return Ok(basket);
    }

    /// <summary>Removes one product from the basket.</summary>
    /// <remarks>
    /// DELETE /api/basket/items/{productId}
    /// Responses: 204 No Content, 401 Unauthorized, 404 Not Found
    /// Authorization: any authenticated user
    ///
    /// Calling it twice gives 204 then 404, and that is not a contradiction.
    /// Idempotency is a promise about SERVER STATE - after any number of
    /// identical DELETEs the product is not in the basket - not a promise that
    /// the status code never changes. The second 404 is simply an accurate
    /// description of a line that is no longer there.
    /// </remarks>
    [HttpDelete("items/{productId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveItem(Guid productId, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var removed = await _basketService.RemoveItemAsync(userId, productId, cancellationToken);

        if (!removed)
        {
            return NotFound();
        }

        return NoContent();
    }

    /// <summary>Empties the basket.</summary>
    /// <remarks>
    /// DELETE /api/basket
    /// Responses: 204 No Content (always), 401 Unauthorized
    /// Authorization: any authenticated user
    ///
    /// Always 204, even for a user who never had a basket. Unlike a single
    /// line, the basket itself always exists conceptually, so "make it empty"
    /// always succeeds.
    /// </remarks>
    [HttpDelete]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Clear(CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        await _basketService.ClearAsync(userId, cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// Reads the caller's id out of the validated token.
    ///
    /// By the time this runs, the JWT middleware has already checked the
    /// signature, the issuer, the audience and the expiry, so the value is
    /// trustworthy in a way nothing from the request body ever is.
    ///
    /// It still returns false rather than assuming: a token could validate and
    /// carry no usable "sub" - a malformed token from a misconfigured issuer,
    /// for instance - and Guid.Parse would throw a 500 for what is really a
    /// 401. Never trust a claim to exist just because the token was valid.
    /// </summary>
    private bool TryGetUserId(out Guid userId)
    {
        var subject = User.FindFirst(JwtClaimNames.Sub)?.Value;

        return Guid.TryParse(subject, out userId);
    }
}
