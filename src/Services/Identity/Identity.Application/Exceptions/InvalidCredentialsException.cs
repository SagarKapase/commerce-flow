namespace Identity.Application.Exceptions;

/// <summary>
/// Thrown when login fails. Maps to 401 Unauthorized.
///
/// THE MESSAGE IS FIXED AND DELIBERATELY VAGUE.
/// "Invalid email or password" is returned whether the email is unknown or the
/// password is wrong. If the two cases produced different responses, an
/// attacker could feed a list of addresses to the login endpoint and learn
/// which ones have accounts - user enumeration - before ever guessing a
/// password. Same reason we do not vary the response time noticeably.
/// </summary>
public sealed class InvalidCredentialsException : IdentityApplicationException
{
    public InvalidCredentialsException()
        : base("Invalid email or password.")
    {
    }
}
