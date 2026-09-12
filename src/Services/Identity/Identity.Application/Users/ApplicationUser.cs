using Microsoft.AspNetCore.Identity;

namespace Identity.Application.Users;

/// <summary>
/// The CommerceFlow user.
///
/// IdentityUser&lt;Guid&gt; already supplies everything authentication needs:
/// Id, UserName, NormalizedUserName, Email, NormalizedEmail, PasswordHash,
/// SecurityStamp, ConcurrencyStamp, LockoutEnd, AccessFailedCount and more.
/// We add only the two fields that are ours.
///
/// WHY Guid AND NOT THE DEFAULT string KEY:
/// The default IdentityUser uses a string primary key holding a GUID's text.
/// Every other service in CommerceFlow identifies things with Guid, and Basket
/// and Ordering will store this id as a user reference, so keeping one type
/// across the whole system avoids a pile of pointless parsing. The cost is the
/// generic parameter you see here and on ApplicationRole and the DbContext.
///
/// WHY PUBLIC SETTERS HERE, WHEN Product HAD PRIVATE ONES:
/// Because we do not own this model. UserManager and the EF Core Identity
/// stores assign these properties directly, so fighting the framework with
/// private setters and factory methods would buy nothing. Follow the
/// conventions of whoever owns the type - that is the lesson, not "always use
/// private setters".
/// </summary>
public sealed class ApplicationUser : IdentityUser<Guid>
{
    public const int FullNameMaxLength = 150;

    public string FullName { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }
}
