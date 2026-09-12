namespace Catalog.Domain.Entities;

/// <summary>
/// A grouping of products, e.g. "Keyboards".
/// </summary>
public sealed class Category
{
    // Length limits live on the entity because the entity owns the rule.
    // The EF configuration and the request DTOs both point at these constants,
    // so there is exactly one place to change if a rule changes.
    public const int NameMaxLength = 100;
    public const int SlugMaxLength = 120;

    /// <summary>
    /// EF Core needs a parameterless constructor to materialise entities read
    /// from the database. It is private so application code cannot create a
    /// half-built Category - the only public way in is <see cref="Create"/>.
    /// The null! assignments tell the compiler "EF will fill these in".
    /// </summary>
    private Category()
    {
        Name = null!;
        Slug = null!;
    }

    private Category(Guid id, string name, string slug, DateTime createdAtUtc)
    {
        Id = id;
        Name = name;
        Slug = slug;
        IsActive = true;
        CreatedAtUtc = createdAtUtc;
    }

    // Private setters: state changes go through the methods below, never by
    // assigning a property from the outside. That is what makes the rules in
    // this class impossible to bypass.
    public Guid Id { get; private set; }

    public string Name { get; private set; }

    /// <summary>URL-friendly identifier, e.g. "mechanical-keyboards". Unique.</summary>
    public string Slug { get; private set; }

    public bool IsActive { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime? UpdatedAtUtc { get; private set; }

    public static Category Create(string name, string slug)
    {
        return new Category(
            // Guid.CreateVersion7 produces a time-ordered GUID (.NET 9+).
            // Random v4 GUIDs arrive in random positions in a clustered/primary
            // key index, which fragments the index as the table grows. v7 GUIDs
            // increase over time, so new rows append instead of scattering.
            id: Guid.CreateVersion7(),
            name: ValidateName(name),
            slug: NormalizeSlug(slug),
            // Using DateTime.UtcNow directly keeps this class dependency-free.
            // If we ever add a time-based *rule* (e.g. "cannot edit after 24h")
            // we would inject a clock so it can be tested; a plain timestamp
            // does not justify that machinery yet.
            createdAtUtc: DateTime.UtcNow);
    }

    public void Update(string name, string slug)
    {
        Name = ValidateName(name);
        Slug = NormalizeSlug(slug);
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void Deactivate()
    {
        if (!IsActive)
        {
            return;
        }

        IsActive = false;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void Activate()
    {
        if (IsActive)
        {
            return;
        }

        IsActive = true;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Slugs are compared for uniqueness, so they must be normalised the same
    /// way everywhere. Exposing this as a public static method means the
    /// application layer can normalise a *search* value with the exact same
    /// rule the entity uses when storing - no duplicated logic to drift.
    /// </summary>
    public static string NormalizeSlug(string slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            throw new ArgumentException("Category slug is required.", nameof(slug));
        }

        var normalized = slug.Trim().ToLowerInvariant();

        if (normalized.Length > SlugMaxLength)
        {
            throw new ArgumentException(
                $"Category slug cannot exceed {SlugMaxLength} characters.",
                nameof(slug));
        }

        return normalized;
    }

    private static string ValidateName(string name)
    {
        // These guards are defence in depth. A real client cannot trigger them,
        // because the request DTO's DataAnnotations reject an empty name with a
        // 400 before we ever get here. If one of these ever throws, it means our
        // own code called Create incorrectly - which is a bug, and a 500 is the
        // honest answer.
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Category name is required.", nameof(name));
        }

        var trimmed = name.Trim();

        if (trimmed.Length > NameMaxLength)
        {
            throw new ArgumentException(
                $"Category name cannot exceed {NameMaxLength} characters.",
                nameof(name));
        }

        return trimmed;
    }
}
