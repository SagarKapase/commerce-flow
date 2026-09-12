namespace Identity.Application.Exceptions;

/// <summary>
/// Thrown when an account is temporarily locked after too many failed sign-in
/// attempts. Maps to 401 Unauthorized.
///
/// Lockout is what stops an attacker simply trying passwords until one works.
/// We do distinguish this case from a plain bad password, which leaks a little
/// (you learn the account exists) - but a user who is locked out and told only
/// "invalid password" will keep trying and stay locked out forever. Usability
/// wins here; the enumeration cost is already paid by the registration endpoint.
/// </summary>
public sealed class AccountLockedException : IdentityApplicationException
{
    public AccountLockedException(DateTimeOffset? lockoutEndsAtUtc)
        : base(lockoutEndsAtUtc is null
            ? "This account is locked."
            : $"This account is locked until {lockoutEndsAtUtc.Value.UtcDateTime:u} due to too many failed sign-in attempts.")
    {
        LockoutEndsAtUtc = lockoutEndsAtUtc;
    }

    public DateTimeOffset? LockoutEndsAtUtc { get; }
}
