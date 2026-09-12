namespace Identity.Application.Exceptions;

/// <summary>
/// Thrown when ASP.NET Core Identity rejects a registration - almost always a
/// password that does not meet the configured policy. Maps to 400 Bad Request
/// with the individual failures listed, so the caller can fix all of them at
/// once instead of discovering them one attempt at a time.
/// </summary>
public sealed class RegistrationFailedException : IdentityApplicationException
{
    public RegistrationFailedException(IReadOnlyList<string> errors)
        : base("Registration failed.")
    {
        Errors = errors;
    }

    public IReadOnlyList<string> Errors { get; }
}
