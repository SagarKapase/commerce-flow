using CommerceFlow.BuildingBlocks.Authentication;
using Catalog.Api.Authentication;
using Catalog.Application.Categories;
using Catalog.Application.Categories.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Catalog.Api.Controllers;

/// <summary>
/// Category management.
///
/// [ApiController] is not decoration. It switches on four behaviours:
///   1. Automatic 400 ValidationProblemDetails when ModelState is invalid, so
///      no action ever needs `if (!ModelState.IsValid)`.
///   2. Binding source inference - complex types come from the body, simple
///      types from the route or query string, without [FromBody] everywhere.
///   3. Attribute routing becomes mandatory (no conventional routes).
///   4. ProblemDetails responses for error status codes.
///
/// [Route] fixes the URL prefix explicitly rather than deriving it from the
/// class name, so renaming the class can never silently change the public API.
///
/// [Authorize(Roles = ...)] at class level, with [AllowAnonymous] on the reads -
/// same secure-by-default arrangement as ProductsController.
/// </summary>
[ApiController]
[Route("api/categories")]
[Produces("application/json")]
[Authorize(Roles = CatalogRoles.Admin)]
public sealed class CategoriesController : ControllerBase
{
    private readonly ICategoryService _categoryService;
    private readonly ILogger<CategoriesController> _logger;

    // Constructor injection. The controller asks for an interface and the DI
    // container supplies the registered implementation; the controller never
    // constructs anything, which is what makes it testable and replaceable.
    public CategoriesController(
        ICategoryService categoryService,
        ILogger<CategoriesController> logger)
    {
        _categoryService = categoryService;
        _logger = logger;
    }

    /// <summary>Lists every category, ordered by name.</summary>
    /// <remarks>
    /// GET /api/categories
    /// Responses: 200 OK
    /// Authorization: anonymous
    /// Breakpoint: CategoriesController.GetAll -> CategoryService.GetAllAsync
    /// </remarks>
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType(typeof(IReadOnlyList<CategoryResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<CategoryResponse>>> GetAll(
        CancellationToken cancellationToken)
    {
        var categories = await _categoryService.GetAllAsync(cancellationToken);

        return Ok(categories);
    }

    /// <summary>Gets one category by id.</summary>
    /// <remarks>
    /// GET /api/categories/{id}
    /// Responses: 200 OK, 404 Not Found
    /// Authorization: anonymous
    /// </remarks>
    [HttpGet("{id:guid}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(CategoryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CategoryResponse>> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        // `{id:guid}` is a route constraint. A request to /api/categories/abc
        // does not match this route at all and returns 404 before any of our
        // code runs - the framework never has to parse a bad Guid.
        var category = await _categoryService.GetByIdAsync(id, cancellationToken);

        if (category is null)
        {
            return NotFound();
        }

        return Ok(category);
    }

    /// <summary>Creates a category.</summary>
    /// <remarks>
    /// POST /api/categories
    /// Body: CreateCategoryRequest
    /// Responses: 201 Created, 400 Bad Request, 401, 403, 409 Conflict (slug taken)
    /// Authorization: Admin
    /// Breakpoint: CategoriesController.Create -> CategoryService.CreateAsync
    /// </remarks>
    [HttpPost]
    [ProducesResponseType(typeof(CategoryResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CategoryResponse>> Create(
        CreateCategoryRequest request,
        CancellationToken cancellationToken)
    {
        var category = await _categoryService.CreateAsync(request, cancellationToken);

        _logger.LogInformation(
            "Category {CategoryId} created by user {UserId}",
            category.Id,
            User.FindFirst(JwtClaimNames.Sub)?.Value);

        // 201 Created, not 200 OK: a new resource now exists at a new URL.
        // CreatedAtAction builds that URL from the GetById action and puts it
        // in the Location response header, so the client is told where the
        // thing it just made lives instead of having to guess.
        return CreatedAtAction(
            nameof(GetById),
            new { id = category.Id },
            category);
    }

    /// <summary>Updates a category's name and slug.</summary>
    /// <remarks>
    /// PUT /api/categories/{id}
    /// Responses: 200 OK, 400, 401, 403, 404 Not Found, 409 Conflict
    /// Authorization: Admin
    /// </remarks>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(CategoryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CategoryResponse>> Update(
        Guid id,
        UpdateCategoryRequest request,
        CancellationToken cancellationToken)
    {
        var category = await _categoryService.UpdateAsync(id, request, cancellationToken);

        if (category is null)
        {
            return NotFound();
        }

        return Ok(category);
    }

    /// <summary>Deactivates a category (soft delete).</summary>
    /// <remarks>
    /// DELETE /api/categories/{id}
    /// Responses: 204 No Content, 401, 403, 404 Not Found,
    ///            409 Conflict (still has active products)
    /// Authorization: Admin
    ///
    /// The action is called Deactivate, not Delete, because that is what it
    /// does. The HTTP verb is DELETE because that is the client's intent;
    /// how we honour it is our business.
    /// </remarks>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken)
    {
        var deactivated = await _categoryService.DeactivateAsync(id, cancellationToken);

        if (!deactivated)
        {
            return NotFound();
        }

        // 204: it worked, and there is nothing meaningful to send back.
        return NoContent();
    }
}
