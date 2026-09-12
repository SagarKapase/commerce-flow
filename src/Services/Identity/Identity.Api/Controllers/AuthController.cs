using Identity.Application.Authentication;
using Identity.Application.Authentication.Dtos;
using Identity.Application.Users.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace Identity.Api.Controllers;

/// <summary>
/// Registration and sign-in.
///
/// Every action here is anonymous - these are the endpoints you use when you
/// have no token yet, so requiring one would be circular.
/// </summary>
[ApiController]
[Route("api/auth")]
[Produces("application/json")]
public sealed class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    /// <summary>Creates a customer account.</summary>
    /// <remarks>
    /// POST /api/auth/register
    /// Body: RegisterRequest
    /// Responses:
    ///   201 Created - Location points at GET /api/users/{id}
    ///   400 Bad Request - shape invalid, or the password fails Identity's policy
    ///   409 Conflict - the email is already registered
    /// Authorization: anonymous
    /// Breakpoint: AuthController.Register -> AuthService.RegisterAsync
    ///
    /// This endpoint deliberately does NOT return tokens. Registration creates
    /// a user; signing in issues credentials. Conflating them gives you an
    /// anonymous endpoint that hands out access tokens, and it hides the fact
    /// that a real system would want email confirmation between the two steps.
    /// </remarks>
    [HttpPost("register")]
    [ProducesResponseType(typeof(UserResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UserResponse>> Register(
        RegisterRequest request,
        CancellationToken cancellationToken)
    {
        var user = await _authService.RegisterAsync(request, cancellationToken);

        // Four-argument overload: action name, CONTROLLER name, route values,
        // body. The created resource is exposed by a different controller, so
        // the controller has to be named explicitly ("Users", without the
        // "Controller" suffix).
        return CreatedAtAction(
            nameof(UsersController.GetById),
            "Users",
            new { id = user.Id },
            user);
    }

    /// <summary>Signs in and returns an access token and a refresh token.</summary>
    /// <remarks>
    /// POST /api/auth/login
    /// Responses: 200 OK, 400 Bad Request, 401 Unauthorized (bad credentials or locked)
    /// Authorization: anonymous
    /// Breakpoint: AuthService.LoginAsync - step over CheckPasswordAsync
    /// </remarks>
    [HttpPost("login")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthResponse>> Login(
        LoginRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _authService.LoginAsync(request, cancellationToken);

        // 200, not 201: signing in does not create a resource at a URL.
        return Ok(response);
    }

    /// <summary>Exchanges a refresh token for a new token pair.</summary>
    /// <remarks>
    /// POST /api/auth/refresh
    /// Responses: 200 OK, 400 Bad Request, 401 Unauthorized
    /// Authorization: anonymous - and it must be. The access token has expired
    /// by the time a client calls this, so requiring a valid one would make the
    /// endpoint unusable exactly when it is needed. The refresh token IS the
    /// credential here.
    ///
    /// The token you send is consumed: the response contains a different
    /// refresh token, and sending the old one again is treated as theft.
    /// </remarks>
    [HttpPost("refresh")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthResponse>> Refresh(
        RefreshTokenRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _authService.RefreshAsync(request, cancellationToken);

        return Ok(response);
    }

    /// <summary>Revokes a refresh token.</summary>
    /// <remarks>
    /// POST /api/auth/logout
    /// Responses: 204 No Content (always, even for an unknown token)
    /// Authorization: anonymous
    ///
    /// Note what logout CANNOT do: invalidate the access token. It is a signed
    /// JWT that every service validates offline, so it stays valid until it
    /// expires - up to 15 minutes here. Killing the refresh token stops the
    /// session from continuing beyond that. If you needed instant revocation
    /// you would have to add a token blacklist, and pay for a lookup on every
    /// single request across every service.
    /// </remarks>
    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(
        RefreshTokenRequest request,
        CancellationToken cancellationToken)
    {
        await _authService.LogoutAsync(request, cancellationToken);

        return NoContent();
    }
}
