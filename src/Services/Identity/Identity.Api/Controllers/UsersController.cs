using System.Security.Claims;
using Identity.Application.Users;
using Identity.Application.Users.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Identity.Api.Controllers;

/// <summary>
/// Reading user accounts. Everything here requires a valid token.
/// </summary>
[ApiController]
[Route("api/users")]
[Produces("application/json")]
public sealed class UsersController : ControllerBase
{
    private readonly IUserService _userService;

    public UsersController(IUserService userService)
    {
        _userService = userService;
    }

    /// <summary>Returns the signed-in user.</summary>
    /// <remarks>
    /// GET /api/users/me
    /// Responses: 200 OK, 401 Unauthorized (no or invalid token)
    /// Authorization: any authenticated user
    /// Breakpoint: UsersController.GetCurrent - inspect User.Claims
    ///
    /// The route is "me", not "{id}", precisely BECAUSE the id comes from the
    /// token. There is no way for a caller to ask for somebody else here.
    /// </remarks>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(typeof(UserResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<UserResponse>> GetCurrent(CancellationToken cancellationToken)
    {
        // THE MOST IMPORTANT LINE IN THIS FILE.
        //
        // The user id comes from the validated token, never from the URL, the
        // query string or the body. A caller cannot forge it without the
        // signing key. Taking an id from the request instead is "broken object
        // level authorization" - consistently the number one item on the OWASP
        // API Security Top 10.
        //
        // The claim is called "sub" and not the long ClaimTypes.NameIdentifier
        // URI because Program.cs sets MapInboundClaims = false.
        var subject = User.FindFirstValue(JwtRegisteredClaimNames.Sub);

        if (!Guid.TryParse(subject, out var userId))
        {
            // The token validated but carries no usable subject - that is a
            // malformed token, not a permissions problem.
            return Unauthorized();
        }

        var user = await _userService.GetByIdAsync(userId, cancellationToken);

        if (user is null)
        {
            // Valid token, but the account is gone. The token outlived it.
            return NotFound();
        }

        return Ok(user);
    }

    /// <summary>Returns any user by id. Administrators only.</summary>
    /// <remarks>
    /// GET /api/users/{id}
    /// Responses: 200 OK, 401 Unauthorized, 403 Forbidden, 404 Not Found
    /// Authorization: Admin role
    ///
    /// 401 vs 403, precisely:
    ///   401 - no token, expired token, bad signature. We do not know who you
    ///         are. Sending better credentials might help.
    ///   403 - your token is perfectly valid and we know exactly who you are.
    ///         You are simply not allowed. Retrying changes nothing.
    ///
    /// The role is checked against the "role" claims inside the token, so this
    /// costs no database lookup - and would work identically in a service that
    /// has no access to the user table at all. That is the whole point of
    /// Phase 3.
    /// </remarks>
    [HttpGet("{id:guid}")]
    [Authorize(Roles = ApplicationRoles.Admin)]
    [ProducesResponseType(typeof(UserResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserResponse>> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var user = await _userService.GetByIdAsync(id, cancellationToken);

        if (user is null)
        {
            return NotFound();
        }

        return Ok(user);
    }
}
