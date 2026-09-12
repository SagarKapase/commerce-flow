namespace Ordering.Api.Authentication;

/// <summary>
/// Roles Ordering makes decisions about.
///
/// Admin appears in two places here: seeing every customer's orders, and
/// cancelling somebody else's. Everything else in this service is scoped by
/// OWNERSHIP rather than role - the question is "is this your order", and the
/// answer comes from the sub claim, not from a role.
///
/// The two mechanisms answer different questions and are not interchangeable:
///   role       - what KIND of user are you? (coarse, in the token)
///   ownership  - is this particular row yours? (fine, in the data)
/// Most real authorization bugs are one used where the other was needed.
/// </summary>
public static class OrderingRoles
{
    public const string Admin = "Admin";
}
