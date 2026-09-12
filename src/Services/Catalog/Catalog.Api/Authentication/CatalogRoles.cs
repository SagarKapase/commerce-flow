namespace Catalog.Api.Authentication;

/// <summary>
/// The roles Catalog makes decisions about.
///
/// There is exactly one, and that is deliberate. Identity knows about both
/// Admin and Customer because it assigns them. Catalog has no rule anywhere
/// that mentions Customer - a signed-in customer and an anonymous visitor can
/// do precisely the same things here - so Catalog does not declare it.
///
/// Resist the urge to "keep them in sync". A service should know the minimum it
/// needs about the rest of the system; every extra shared concept is another
/// thing that has to change in two places when it changes at all.
/// </summary>
public static class CatalogRoles
{
    public const string Admin = "Admin";
}
