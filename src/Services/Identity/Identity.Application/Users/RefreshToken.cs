using System.Security.Cryptography;
using System.Text;

namespace Identity.Application.Users;

/// <summary>
/// A long-lived credential that can be exchanged for a new access token.
///
/// WHY REFRESH TOKENS EXIST AT ALL:
/// A JWT cannot be revoked - every service validates it locally with a signing
/// key and never asks Identity whether it is still good. So the access token
/// must be SHORT lived (15 minutes here), or a stolen one is valid for its full
/// lifetime. But forcing a user to retype their password every 15 minutes is
/// unusable. The refresh token squares that circle: it lives in OUR database,
/// so unlike the JWT it CAN be revoked, and it is the only thing that survives
/// long term.
///
/// This is the one type in the Identity service with real rules of its own,
/// which is why it gets private setters and factory methods while
/// ApplicationUser does not - we own this one.
/// </summary>
public sealed class RefreshToken
{
    /// <summary>Base64 of a SHA-256 hash is always 44 characters.</summary>
    public const int TokenHashLength = 44;

    /// <summary>Constructor used only by EF Core when reading rows.</summary>
    private RefreshToken()
    {
        TokenHash = null!;
    }

    private RefreshToken(
        Guid id,
        Guid userId,
        string tokenHash,
        DateTime createdAtUtc,
        DateTime expiresAtUtc)
    {
        Id = id;
        UserId = userId;
        TokenHash = tokenHash;
        CreatedAtUtc = createdAtUtc;
        ExpiresAtUtc = expiresAtUtc;
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    /// <summary>
    /// The SHA-256 hash of the token, never the token itself.
    ///
    /// If our database leaks, an attacker with these hashes cannot authenticate
    /// as anyone - exactly the reason passwords are hashed. Note we use plain
    /// SHA-256 here and NOT the slow, salted hash used for passwords: this
    /// value is 256 bits of cryptographic randomness, so there is no dictionary
    /// to attack and nothing for a slow hash to buy us.
    /// </summary>
    public string TokenHash { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime ExpiresAtUtc { get; private set; }

    /// <summary>Set when the token is used (rotation), or on logout.</summary>
    public DateTime? RevokedAtUtc { get; private set; }

    /// <summary>
    /// Which token replaced this one. Keeping the chain is what makes reuse
    /// detection possible: if a REVOKED token is presented again, either it was
    /// stolen or the real client replayed it, and the safe response is to kill
    /// the whole family.
    /// </summary>
    public Guid? ReplacedByTokenId { get; private set; }

    public bool IsActive(DateTime utcNow)
    {
        return RevokedAtUtc is null && utcNow < ExpiresAtUtc;
    }

    /// <summary>
    /// Creates a new refresh token.
    /// </summary>
    /// <returns>
    /// The entity to store AND the raw value to return to the client. The raw
    /// value exists only in this tuple and in the HTTP response - it is never
    /// written to the database, so it cannot be recovered later, not even by us.
    /// </returns>
    public static (RefreshToken Token, string RawValue) Issue(
        Guid userId,
        TimeSpan lifetime,
        DateTime utcNow)
    {
        // 32 bytes = 256 bits from a cryptographically secure generator.
        // Random or Guid.NewGuid would be guessable enough to matter here.
        var rawBytes = RandomNumberGenerator.GetBytes(32);
        var rawValue = Convert.ToBase64String(rawBytes);

        var token = new RefreshToken(
            id: Guid.CreateVersion7(),
            userId: userId,
            tokenHash: ComputeHash(rawValue),
            createdAtUtc: utcNow,
            expiresAtUtc: utcNow.Add(lifetime));

        return (token, rawValue);
    }

    public void Revoke(DateTime utcNow, Guid? replacedByTokenId = null)
    {
        // Revoking twice is not an error - logging out twice should not fail.
        if (RevokedAtUtc is not null)
        {
            return;
        }

        RevokedAtUtc = utcNow;
        ReplacedByTokenId = replacedByTokenId;
    }

    /// <summary>
    /// Hashes a raw token so it can be looked up. Deterministic on purpose:
    /// we need to find the row by hash, which a salted hash would prevent.
    /// </summary>
    public static string ComputeHash(string rawToken)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));

        return Convert.ToBase64String(hash);
    }
}
