using Identity.Application.Abstractions;
using Identity.Application.Authentication.Dtos;
using Identity.Application.Exceptions;
using Identity.Application.Users;
using Identity.Application.Users.Dtos;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Identity.Application.Authentication;

public sealed class AuthService : IAuthService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IIdentityDbContext _dbContext;
    private readonly IAccessTokenGenerator _accessTokenGenerator;
    private readonly RefreshTokenOptions _refreshTokenOptions;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        UserManager<ApplicationUser> userManager,
        IIdentityDbContext dbContext,
        IAccessTokenGenerator accessTokenGenerator,
        IOptions<RefreshTokenOptions> refreshTokenOptions,
        ILogger<AuthService> logger)
    {
        _userManager = userManager;
        _dbContext = dbContext;
        _accessTokenGenerator = accessTokenGenerator;
        _refreshTokenOptions = refreshTokenOptions.Value;
        _logger = logger;
    }

    public async Task<UserResponse> RegisterAsync(
        RegisterRequest request,
        CancellationToken cancellationToken)
    {
        var user = new ApplicationUser
        {
            Id = Guid.CreateVersion7(),

            // UserName is required by Identity and must be unique. We have no
            // separate display handle in this system, so the email doubles as
            // the user name - a common choice that keeps one thing unique
            // instead of two.
            UserName = request.Email,
            Email = request.Email,
            FullName = request.FullName.Trim(),
            CreatedAtUtc = DateTime.UtcNow
        };

        // CreateAsync does a great deal for us: it normalises the email and
        // user name, runs every registered IUserValidator and
        // IPasswordValidator, HASHES the password with PBKDF2 (100k+
        // iterations, per-user salt, versioned format), sets the security
        // stamp and saves the row.
        //
        // We never see or store the plaintext password, and we never write
        // hashing code. Rolling your own here is the single most reliable way
        // to fail a security review.
        //
        // Note: UserManager methods do not accept a CancellationToken - it has
        // its own internal one. Our token still guards everything else.
        var result = await _userManager.CreateAsync(user, request.Password);

        if (!result.Succeeded)
        {
            // Identity reports failures as machine-readable codes. Treating a
            // duplicate email as a distinct outcome lets us return 409 for it
            // and 400 for "your password is too weak" - two genuinely different
            // problems for the caller.
            var isDuplicate = result.Errors.Any(error =>
                error.Code == nameof(IdentityErrorDescriber.DuplicateEmail) ||
                error.Code == nameof(IdentityErrorDescriber.DuplicateUserName));

            if (isDuplicate)
            {
                throw new EmailAlreadyRegisteredException(request.Email);
            }

            throw new RegistrationFailedException(
                result.Errors.Select(error => error.Description).ToList());
        }

        // Everyone who registers through the public endpoint is a Customer.
        // Admins are seeded, never self-service - an endpoint that lets a
        // caller choose their own role is a privilege-escalation hole.
        await _userManager.AddToRoleAsync(user, ApplicationRoles.Customer);

        _logger.LogInformation("Registered user {UserId}", user.Id);

        return UserMappings.ToResponse(user, new[] { ApplicationRoles.Customer });
    }

    public async Task<AuthResponse> LoginAsync(
        LoginRequest request,
        CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByEmailAsync(request.Email);

        if (user is null)
        {
            // Same exception, same message, same status code as a wrong
            // password. An attacker must not be able to tell "no such account"
            // from "wrong password" - see InvalidCredentialsException.
            throw new InvalidCredentialsException();
        }

        // Check lockout BEFORE checking the password: a locked account must not
        // be usable even by someone who has since guessed the right password.
        if (await _userManager.IsLockedOutAsync(user))
        {
            _logger.LogWarning("Sign-in attempt on locked account {UserId}", user.Id);

            throw new AccountLockedException(user.LockoutEnd);
        }

        // CheckPasswordAsync hashes the supplied password with the same salt
        // and parameters as the stored hash and compares them in constant time.
        if (!await _userManager.CheckPasswordAsync(user, request.Password))
        {
            // This is the whole brute-force defence, written out rather than
            // hidden inside SignInManager: count the failure, and Identity
            // locks the account once the configured threshold is reached.
            await _userManager.AccessFailedAsync(user);

            if (await _userManager.IsLockedOutAsync(user))
            {
                _logger.LogWarning("Account {UserId} locked after repeated failures", user.Id);

                throw new AccountLockedException(user.LockoutEnd);
            }

            _logger.LogWarning("Failed sign-in for user {UserId}", user.Id);

            throw new InvalidCredentialsException();
        }

        // A successful sign-in clears the counter, so five failures spread over
        // a year do not eventually lock a legitimate user out.
        await _userManager.ResetAccessFailedCountAsync(user);

        _logger.LogInformation("User {UserId} signed in", user.Id);

        return await IssueTokensAsync(user, tokenBeingReplaced: null, cancellationToken);
    }

    public async Task<AuthResponse> RefreshAsync(
        RefreshTokenRequest request,
        CancellationToken cancellationToken)
    {
        // We stored a hash, so we search by hash. The raw token the client
        // sent exists nowhere in our database.
        var tokenHash = RefreshToken.ComputeHash(request.RefreshToken);

        var storedToken = await _dbContext.RefreshTokens
            .FirstOrDefaultAsync(token => token.TokenHash == tokenHash, cancellationToken);

        if (storedToken is null)
        {
            throw new InvalidRefreshTokenException();
        }

        var utcNow = DateTime.UtcNow;

        if (!storedToken.IsActive(utcNow))
        {
            // REUSE DETECTION.
            //
            // Every refresh rotates: using a token immediately revokes it. So a
            // request presenting an ALREADY-REVOKED token means the same token
            // was used twice, and there are only two explanations - it was
            // stolen and the thief is now using it, or it was stolen and the
            // real user is. Either way one of the two parties is an attacker
            // and we cannot tell which, so we end every session for that user
            // and force a fresh sign-in.
            if (storedToken.RevokedAtUtc is not null)
            {
                await RevokeAllActiveTokensAsync(storedToken.UserId, utcNow, cancellationToken);

                _logger.LogWarning(
                    "Refresh token reuse detected for user {UserId}; all sessions revoked",
                    storedToken.UserId);
            }

            throw new InvalidRefreshTokenException();
        }

        var user = await _userManager.FindByIdAsync(storedToken.UserId.ToString());

        if (user is null)
        {
            // The account was deleted while a token was still live.
            throw new InvalidRefreshTokenException();
        }

        return await IssueTokensAsync(user, storedToken, cancellationToken);
    }

    public async Task LogoutAsync(RefreshTokenRequest request, CancellationToken cancellationToken)
    {
        var tokenHash = RefreshToken.ComputeHash(request.RefreshToken);

        var storedToken = await _dbContext.RefreshTokens
            .FirstOrDefaultAsync(token => token.TokenHash == tokenHash, cancellationToken);

        // Deliberately silent about an unknown token: logout always reports
        // success, so it cannot be used to test whether a token is real.
        if (storedToken is null)
        {
            return;
        }

        storedToken.Revoke(DateTime.UtcNow);

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("User {UserId} signed out", storedToken.UserId);
    }

    /// <summary>
    /// Issues a fresh access + refresh token pair, optionally retiring the
    /// token that was just used.
    /// </summary>
    private async Task<AuthResponse> IssueTokensAsync(
        ApplicationUser user,
        RefreshToken? tokenBeingReplaced,
        CancellationToken cancellationToken)
    {
        // Roles are read now and BAKED INTO the token. That is the trade-off of
        // stateless JWTs: granting or removing a role does not affect tokens
        // already issued, so a demoted admin keeps admin rights until their
        // access token expires. Short access-token lifetimes are what keep that
        // window small.
        var roles = (await _userManager.GetRolesAsync(user)).ToList();

        var accessToken = _accessTokenGenerator.Generate(
            user.Id,
            user.Email ?? string.Empty,
            user.FullName,
            roles);

        var utcNow = DateTime.UtcNow;

        var (refreshToken, rawRefreshTokenValue) = RefreshToken.Issue(
            user.Id,
            _refreshTokenOptions.Lifetime,
            utcNow);

        _dbContext.RefreshTokens.Add(refreshToken);

        // ROTATION: the token that was just used is retired and linked to its
        // successor. A refresh token is therefore single-use.
        tokenBeingReplaced?.Revoke(utcNow, refreshToken.Id);

        // One SaveChangesAsync, one transaction: the new token is stored and
        // the old one revoked together, or neither happens. If this were two
        // calls, a crash in between could leave the user with no usable token
        // at all, or with two live ones.
        await _dbContext.SaveChangesAsync(cancellationToken);

        return new AuthResponse(
            accessToken.Value,
            accessToken.ExpiresAtUtc,
            rawRefreshTokenValue,
            refreshToken.ExpiresAtUtc,
            user.Id,
            user.Email ?? string.Empty,
            user.FullName,
            roles);
    }

    private async Task RevokeAllActiveTokensAsync(
        Guid userId,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        var activeTokens = await _dbContext.RefreshTokens
            .Where(token => token.UserId == userId && token.RevokedAtUtc == null)
            .ToListAsync(cancellationToken);

        foreach (var token in activeTokens)
        {
            token.Revoke(utcNow);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
