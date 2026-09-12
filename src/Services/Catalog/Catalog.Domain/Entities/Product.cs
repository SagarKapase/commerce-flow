namespace Catalog.Domain.Entities;

/// <summary>
/// Something a customer can buy. The Catalog owns what a product *is*
/// (name, description, SKU, price, category). It does NOT own how many are in
/// stock - that belongs to the Inventory service, which has its own database.
/// </summary>
public sealed class Product
{
    public const int NameMaxLength = 200;
    public const int DescriptionMaxLength = 2000;
    public const int SkuMaxLength = 50;

    public const decimal MinimumPrice = 0.01m;
    public const decimal MaximumPrice = 1_000_000m;

    /// <summary>Constructor used only by EF Core when reading rows.</summary>
    private Product()
    {
        Name = null!;
        Description = null!;
        Sku = null!;
    }

    private Product(
        Guid id,
        string name,
        string description,
        string sku,
        decimal price,
        Guid categoryId,
        DateTime createdAtUtc)
    {
        Id = id;
        Name = name;
        Description = description;
        Sku = sku;
        Price = price;
        CategoryId = categoryId;
        IsActive = true;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; }

    public string Description { get; private set; }

    /// <summary>Stock Keeping Unit. Unique across the catalog, stored upper-case.</summary>
    public string Sku { get; private set; }

    /// <summary>
    /// Money is a decimal here - exact base-10 arithmetic, no floating-point
    /// surprises. How it is *stored* is a persistence concern and lives in
    /// ProductConfiguration (SQLite has no decimal type).
    /// </summary>
    public decimal Price { get; private set; }

    public Guid CategoryId { get; private set; }

    /// <summary>
    /// Navigation property. Nullable because it is only populated when a query
    /// explicitly asks for it (Include or a projection). We deliberately did NOT
    /// add a Category.Products collection on the other side: nothing in this
    /// service needs it, and having one invites accidentally loading every
    /// product of a category into memory.
    /// </summary>
    public Category? Category { get; private set; }

    /// <summary>
    /// Soft-delete flag. Products are never physically deleted: orders placed in
    /// the past refer to them, and a catalog row that vanishes would break order
    /// history and reporting.
    /// </summary>
    public bool IsActive { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime? UpdatedAtUtc { get; private set; }

    public static Product Create(
        string name,
        string description,
        string sku,
        decimal price,
        Guid categoryId)
    {
        return new Product(
            id: Guid.CreateVersion7(),
            name: ValidateName(name),
            description: ValidateDescription(description),
            sku: NormalizeSku(sku),
            price: ValidatePrice(price),
            categoryId: ValidateCategoryId(categoryId),
            createdAtUtc: DateTime.UtcNow);
    }

    public void Update(
        string name,
        string description,
        string sku,
        decimal price,
        Guid categoryId)
    {
        Name = ValidateName(name);
        Description = ValidateDescription(description);
        Sku = NormalizeSku(sku);
        Price = ValidatePrice(price);
        CategoryId = ValidateCategoryId(categoryId);
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void Deactivate()
    {
        // Deactivating an already-inactive product is not an error. That makes
        // DELETE idempotent: sending it twice leaves the same end state, which
        // is exactly what the HTTP spec expects of DELETE.
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
    /// SKUs are compared for uniqueness, so "key-mx-001" and "KEY-MX-001" must
    /// not be two different products. Normalising in one public place means the
    /// application layer checks for duplicates using the same rule the entity
    /// uses when it stores the value.
    /// </summary>
    public static string NormalizeSku(string sku)
    {
        if (string.IsNullOrWhiteSpace(sku))
        {
            throw new ArgumentException("Product SKU is required.", nameof(sku));
        }

        var normalized = sku.Trim().ToUpperInvariant();

        if (normalized.Length > SkuMaxLength)
        {
            throw new ArgumentException(
                $"Product SKU cannot exceed {SkuMaxLength} characters.",
                nameof(sku));
        }

        return normalized;
    }

    private static string ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Product name is required.", nameof(name));
        }

        var trimmed = name.Trim();

        if (trimmed.Length > NameMaxLength)
        {
            throw new ArgumentException(
                $"Product name cannot exceed {NameMaxLength} characters.",
                nameof(name));
        }

        return trimmed;
    }

    private static string ValidateDescription(string description)
    {
        // Description is optional from a business point of view; an empty string
        // is a legitimate value, so we normalise null to empty rather than throw.
        var trimmed = (description ?? string.Empty).Trim();

        if (trimmed.Length > DescriptionMaxLength)
        {
            throw new ArgumentException(
                $"Product description cannot exceed {DescriptionMaxLength} characters.",
                nameof(description));
        }

        return trimmed;
    }

    private static decimal ValidatePrice(decimal price)
    {
        if (price < MinimumPrice || price > MaximumPrice)
        {
            throw new ArgumentOutOfRangeException(
                nameof(price),
                price,
                $"Product price must be between {MinimumPrice} and {MaximumPrice}.");
        }

        // We store money as integer minor units, so anything finer than two
        // decimal places cannot be represented exactly. Rejecting it is honest;
        // silently rounding a price is the kind of bug nobody notices until
        // an invoice is wrong.
        if (decimal.Round(price, 2) != price)
        {
            throw new ArgumentException(
                "Product price cannot have more than two decimal places.",
                nameof(price));
        }

        return price;
    }

    private static Guid ValidateCategoryId(Guid categoryId)
    {
        if (categoryId == Guid.Empty)
        {
            throw new ArgumentException("Product must belong to a category.", nameof(categoryId));
        }

        return categoryId;
    }
}
