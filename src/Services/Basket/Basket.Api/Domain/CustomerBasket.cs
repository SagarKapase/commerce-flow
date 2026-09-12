using Basket.Api.Exceptions;

namespace Basket.Api.Domain;

/// <summary>
/// One basket per user.
///
/// WHY "CustomerBasket" AND NOT "Basket":
/// The project's root namespace is Basket.Api, so a type called Basket would
/// collide with the namespace segment `Basket` and make half the file ambiguous
/// to the compiler. Microsoft's own eShopOnContainers sample hit this and made
/// the same rename. Worth knowing: namespace/type collisions are a real design
/// constraint, not a style preference.
///
/// THIS IS AN AGGREGATE ROOT. Items are reachable only through it, the
/// collection is exposed read-only, and every rule about the basket as a whole
/// - how many distinct lines, how many of each - lives here. It is deliberately
/// the same shape as the Order aggregate we build in Phase 7, on a much easier
/// problem.
/// </summary>
public sealed class CustomerBasket
{
    /// <summary>
    /// A basket with 50 different products is a bug or an attack, not shopping.
    /// Unbounded collections are how a per-user document quietly becomes a
    /// denial-of-service vector.
    /// </summary>
    public const int MaxDistinctItems = 50;

    // The backing field is the real collection; Items below is a read-only
    // window onto it. Callers cannot do basket.Items.Add(...) and skip the
    // rules - which is the entire point of an aggregate root.
    private readonly List<BasketItem> _items = new();

    private CustomerBasket()
    {
    }

    private CustomerBasket(Guid userId, DateTime createdAtUtc)
    {
        UserId = userId;
        CreatedAtUtc = createdAtUtc;
    }

    /// <summary>
    /// The primary key IS the user id.
    ///
    /// There is no surrogate Id column, on purpose. A basket is identified by
    /// its owner: there is never a basket without a user and never two baskets
    /// for one user, so the primary key enforces that invariant by itself - no
    /// extra unique index, no surrogate nothing outside this service would ever
    /// reference.
    ///
    /// It also makes the eventual Redis migration obvious: this is a key-value
    /// document whose key is already the user id.
    /// </summary>
    public Guid UserId { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime? UpdatedAtUtc { get; private set; }

    public IReadOnlyCollection<BasketItem> Items => _items.AsReadOnly();

    public int TotalQuantity => _items.Sum(item => item.Quantity);

    public decimal TotalAmount => _items.Sum(item => item.LineTotal);

    public static CustomerBasket Create(Guid userId)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("A basket must belong to a user.", nameof(userId));
        }

        return new CustomerBasket(userId, DateTime.UtcNow);
    }

    /// <summary>
    /// Adds a product, or increases the quantity if it is already there.
    ///
    /// "Add the same product twice" must never produce two lines for the same
    /// product - that is why the item's primary key is (UserId, ProductId).
    /// The database enforces what this method intends.
    /// </summary>
    public void AddItem(Guid productId, string productName, decimal unitPrice, int quantity)
    {
        BasketItem.ValidateQuantity(quantity);

        var utcNow = DateTime.UtcNow;
        var existing = FindItem(productId);

        if (existing is not null)
        {
            var newQuantity = existing.Quantity + quantity;

            if (newQuantity > BasketItem.MaxQuantity)
            {
                throw new BasketLimitExceededException(
                    $"A basket line cannot exceed {BasketItem.MaxQuantity} units. " +
                    $"This line already has {existing.Quantity}.");
            }

            existing.ChangeQuantity(newQuantity, utcNow);

            // The price and name are refreshed from what the caller supplied,
            // so a basket always reflects the most recent information we had.
            existing.RefreshProductDetails(productName, unitPrice, utcNow);
        }
        else
        {
            if (_items.Count >= MaxDistinctItems)
            {
                throw new BasketLimitExceededException(
                    $"A basket cannot hold more than {MaxDistinctItems} different products.");
            }

            _items.Add(new BasketItem(
                UserId, productId, productName, unitPrice, quantity, utcNow));
        }

        UpdatedAtUtc = utcNow;
    }

    /// <summary>Sets an existing line to an exact quantity.</summary>
    /// <returns><c>false</c> when the product is not in the basket.</returns>
    public bool UpdateItemQuantity(Guid productId, int quantity)
    {
        var existing = FindItem(productId);

        if (existing is null)
        {
            return false;
        }

        existing.ChangeQuantity(quantity, DateTime.UtcNow);
        UpdatedAtUtc = DateTime.UtcNow;

        return true;
    }

    /// <returns><c>false</c> when the product is not in the basket.</returns>
    public bool RemoveItem(Guid productId)
    {
        var existing = FindItem(productId);

        if (existing is null)
        {
            return false;
        }

        _items.Remove(existing);
        UpdatedAtUtc = DateTime.UtcNow;

        return true;
    }

    public void Clear()
    {
        // Clearing an empty basket is not an error - it leaves the same end
        // state, which is exactly what DELETE is supposed to guarantee.
        if (_items.Count == 0)
        {
            return;
        }

        _items.Clear();
        UpdatedAtUtc = DateTime.UtcNow;
    }

    private BasketItem? FindItem(Guid productId)
    {
        return _items.FirstOrDefault(item => item.ProductId == productId);
    }
}
