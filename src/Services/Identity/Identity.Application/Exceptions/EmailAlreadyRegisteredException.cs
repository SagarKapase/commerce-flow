namespace Identity.Application.Exceptions;

/// <summary>
/// Thrown when registration is attempted with an email that already exists.
/// Maps to 409 Conflict.
///
/// A NOTE ON USER ENUMERATION:
/// This response does tell an attacker that an email is registered. That is a
/// real trade-off, and the usual mitigation is to always return 202 Accepted
/// and send a "someone tried to register with your address" email instead. We
/// return 409 because this project has no email delivery and because a
/// registration form that cannot say "you already have an account" is close to
/// unusable. What matters is knowing the trade-off exists and having chosen it
/// deliberately - notice that LOGIN, further down, does not leak this.
/// </summary>
public sealed class EmailAlreadyRegisteredException : IdentityApplicationException
{
    public EmailAlreadyRegisteredException(string email)
        : base($"An account with email '{email}' already exists.")
    {
        Email = email;
    }

    public string Email { get; }
}
