using Catalog.Api.Authentication;
using CommerceFlow.BuildingBlocks.Authentication;
using Catalog.Application.Common;
using Catalog.Application.Products;
using Catalog.Application.Products.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Catalog.Api.Controllers;

/// <summary>
/// Product catalog.
///
/// Every action here is thin on purpose: bind the request, call the
/// application service, translate the result into a status code. There is no
/// EF Core, no LINQ over the database and no business rule in this file - if
/// one appears, it is in the wrong place.
///
/// AUTHORIZATION (Phase 3): the whole controller requires the Admin role, and
/// the read endpoints opt back out with [AllowAnonymous].
///
/// WHY THAT WAY ROUND, RATHER THAN [Authorize] ON EACH WRITE:
/// It fails closed. Add a new endpoint here in six months and forget to think
/// about security, and it is protected by default - the mistake costs you a
/// bug report, not a breach. The other arrangement fails open: forget the
/// attribute and the endpoint is silently public.
///
/// The precedence rule is worth knowing exactly: [AllowAnonymous] ALWAYS wins
/// over [Authorize], wherever each is declared. That is what makes this pattern
/// work - and it is also a trap, because an [AllowAnonymous] left on a
/// controller cannot be overridden by an [Authorize] on an action inside it.
/// </summary>
[ApiController]
[Route("api/products")]
[Produces("application/json")]
[Authorize(Roles = CatalogRoles.Admin)]
public sealed class ProductsController : ControllerBase
{
    private readonly IProductService _productService;
    private readonly ILogger<ProductsController> _logger;

    public ProductsController(
        IProductService productService,
        ILogger<ProductsController> logger)
    {
        _productService = productService;
        _logger = logger;
    }

    /// <summary>Lists products with filtering and pagination.</summary>
    /// <remarks>
    /// GET /api/products?search=keyboard&amp;categoryId=...&amp;minPrice=10&amp;maxPrice=100&amp;isActive=true&amp;page=1&amp;pageSize=20
    ///
    /// Every parameter is optional. Responses: 200 OK, 400 Bad Request (bad paging).
    /// Authorization: anonymous - this is a public shop front.
    /// Breakpoint: ProductsController.GetAll -> ProductService.GetAsync
    /// </remarks>
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType(typeof(PagedResponse<ProductResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResponse<ProductResponse>>> GetAll(
        // [FromQuery] on a complex type is explicit here even though
        // [ApiController] would infer it. Being able to see at a glance where
        // a parameter comes from is worth one attribute.
        [FromQuery] ProductListQuery query,
        CancellationToken cancellationToken)
    {
        var products = await _productService.GetAsync(query, cancellationToken);

        return Ok(products);
    }

    /// <summary>Gets one product by id.</summary>
    /// <remarks>
    /// GET /api/products/{id}
    /// Responses: 200 OK, 404 Not Found
    /// Authorization: anonymous
    /// </remarks>
    [HttpGet("{id:guid}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ProductResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProductResponse>> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var product = await _productService.GetByIdAsync(id, cancellationToken);

        if (product is null)
        {
            // 404 says "this URL identifies nothing". It is not an error in the
            // client's request format, which is why it is not a 400.
            return NotFound();
        }

        return Ok(product);
    }

    /// <summary>Creates a product.</summary>
    /// <remarks>
    /// POST /api/products
    /// Body: CreateProductRequest
    /// Responses:
    ///   201 Created - Location header points at GET /api/products/{id}
    ///   400 Bad Request - validation failed, or categoryId does not exist
    ///   401 Unauthorized - no token, expired token, or a bad signature
    ///   403 Forbidden - a valid token without the Admin role
    ///   409 Conflict - the SKU is already taken
    /// Authorization: Admin
    ///
    /// Breakpoints: ProductsController.Create -> ProductService.CreateAsync
    /// Watch: User.Claims, request, product, product.Id, product.Sku, product.Price
    /// </remarks>
    [HttpPost]
    [ProducesResponseType(typeof(ProductResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ProductResponse>> Create(
        // Model binding reads the request body, deserialises the JSON into this
        // object, then runs every DataAnnotation on it. If any fail, this method
        // is never entered - [ApiController] has already returned a 400.
        CreateProductRequest request,
        // The token is cancelled when the client disconnects, and we pass it all
        // the way down to the database call. Without it, a user who closes their
        // browser mid-request leaves the server finishing work nobody wants.
        CancellationToken cancellationToken)
    {
        var product = await _productService.CreateAsync(request, cancellationToken);

        // Catalog now knows WHO made this change, and it learned that from the
        // token alone - no lookup, no call to Identity, no users table in
        // catalog.db. Worth pausing on: an audit trail across service
        // boundaries costs one claim read.
        _logger.LogInformation(
            "Product {ProductId} created via API by user {UserId}",
            product.Id,
            User.FindFirst(JwtClaimNames.Sub)?.Value);

        return CreatedAtAction(
            nameof(GetById),
            new { id = product.Id },
            product);
    }

    /// <summary>Replaces the editable fields of a product.</summary>
    /// <remarks>
    /// PUT /api/products/{id}
    /// Responses: 200 OK, 400, 401, 403, 404 Not Found, 409 Conflict
    /// Authorization: Admin
    /// </remarks>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(ProductResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ProductResponse>> Update(
        Guid id,
        UpdateProductRequest request,
        CancellationToken cancellationToken)
    {
        var product = await _productService.UpdateAsync(id, request, cancellationToken);

        if (product is null)
        {
            return NotFound();
        }

        _logger.LogInformation(
            "Product {ProductId} updated by user {UserId}",
            product.Id,
            User.FindFirst(JwtClaimNames.Sub)?.Value);

        return Ok(product);
    }

    /// <summary>Deactivates a product (soft delete).</summary>
    /// <remarks>
    /// DELETE /api/products/{id}
    /// Responses: 204 No Content, 401, 403, 404 Not Found
    /// Authorization: Admin
    ///
    /// The row is kept and IsActive is set to 0. Orders placed in the past
    /// reference this product; a catalog row that disappears would break order
    /// history in a service that does not even share our database.
    ///
    /// Calling this twice is safe - the second call still returns 204, because
    /// DELETE is expected to be idempotent.
    /// </remarks>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken)
    {
        var deactivated = await _productService.DeactivateAsync(id, cancellationToken);

        if (!deactivated)
        {
            return NotFound();
        }

        _logger.LogInformation(
            "Product {ProductId} deactivated by user {UserId}",
            id,
            User.FindFirst(JwtClaimNames.Sub)?.Value);

        return NoContent();
    }
}
