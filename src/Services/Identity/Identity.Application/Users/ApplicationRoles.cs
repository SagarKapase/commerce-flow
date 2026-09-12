namespace Identity.Application.Users;

/// <summary>
/// The role names, as constants.
///
/// Roles are compared as strings by [Authorize(Roles = "...")], and a typo in a
/// magic string fails silently - the attribute simply never matches and every
/// request gets a 403 that looks like a permissions bug. Constants turn that
/// into a compile error.
///
/// These same two strings will appear in Catalog, Ordering and the Gateway in
/// later phases. They will be DUPLICATED there, not shared through a common
/// library: a shared constants assembly is the first step towards a shared
/// domain assembly, which is how microservices quietly become a distributed
/// monolith that has to be deployed all at once.
/// </summary>
public static class ApplicationRoles
{
    public const string Admin = "Admin";

    public const string Customer = "Customer";
}
