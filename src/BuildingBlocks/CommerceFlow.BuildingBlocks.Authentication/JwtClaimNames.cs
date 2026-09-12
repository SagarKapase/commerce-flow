namespace CommerceFlow.BuildingBlocks.Authentication;

/// <summary>
/// The claim names CommerceFlow services read out of a validated token.
///
/// These must match exactly what Identity writes in
/// JwtAccessTokenGenerator. If Identity emitted "roles" and a consumer looked
/// for "role", the token would validate perfectly and every admin would still
/// get a 403 - with nothing in the logs to explain why. That failure mode is
/// the single best argument for these being constants in one place now that
/// four services depend on them.
///
/// Only the two that are actually read. There is no "email" or "jti" constant,
/// because no service reads those claims - and a shared library should contain
/// what is used, not what might be.
/// </summary>
public static class JwtClaimNames
{
    /// <summary>The user id. Standard registered claim.</summary>
    public const string Sub = "sub";

    /// <summary>
    /// Role membership. NOT a standard registered claim - we chose the short
    /// name over the 70-character ClaimTypes.Role URI, so both sides have to
    /// agree on it explicitly.
    /// </summary>
    public const string Role = "role";
}
