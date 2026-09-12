namespace Inventory.Api.Authentication;

/// <summary>
/// The roles Inventory makes decisions about.
///
/// JwtOptions and JwtClaimNames used to live beside this file. In Phase 7 they
/// moved to CommerceFlow.BuildingBlocks.Authentication, because how to validate
/// a token is identical in every service. This did NOT move, and the line
/// between them is the interesting part:
///
///   MECHANISM is shared - reading a bearer header, checking a signature,
///   validating issuer, audience and lifetime. Identical everywhere, and a
///   change to it (moving to RS256, say) genuinely should touch every service
///   at once.
///
///   POLICY is not - which roles this particular service cares about. Inventory
///   needs Admin for stock adjustments. Basket needs no roles at all. Catalog
///   needs Admin for writes. A shared Roles class would force Basket to depend
///   on a concept it never uses, and adding a fourth role to the system would
///   mean redeploying every service that referenced the constant.
///
/// The question to ask before putting anything in BuildingBlocks: "if this
/// changes, must every service be rebuilt together?" For token validation the
/// answer is yes and that is fine. For business vocabulary it is yes and that
/// is a distributed monolith.
/// </summary>
public static class InventoryRoles
{
    public const string Admin = "Admin";
}
