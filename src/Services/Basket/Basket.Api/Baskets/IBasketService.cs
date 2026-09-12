using Basket.Api.Dtos;

namespace Basket.Api.Baskets;

/// <summary>
/// Basket use cases.
///
/// EVERY METHOD TAKES userId AS ITS FIRST PARAMETER, and no method takes it
/// from anywhere else. That is not a style choice - it means this service
/// physically cannot operate on "the basket" without being told whose. The
/// controller is the only place that decides which user, and it reads that from
/// the validated token.
/// </summary>
public interface IBasketService
{
    /// <summary>
    /// Returns the user's basket, or an empty one if they have never added
    /// anything. Never null, never 404.
    /// </summary>
    Task<BasketResponse> GetAsync(Guid userId, CancellationToken cancellationToken);

    Task<BasketResponse> AddItemAsync(
        Guid userId,
        AddBasketItemRequest request,
        CancellationToken cancellationToken);

    /// <returns>The updated basket, or <c>null</c> when that product is not in it.</returns>
    Task<BasketResponse?> UpdateItemQuantityAsync(
        Guid userId,
        Guid productId,
        int quantity,
        CancellationToken cancellationToken);

    /// <returns><c>false</c> when that product is not in the basket.</returns>
    Task<bool> RemoveItemAsync(Guid userId, Guid productId, CancellationToken cancellationToken);

    /// <summary>Empties the basket. Safe to call on an already-empty one.</summary>
    Task ClearAsync(Guid userId, CancellationToken cancellationToken);
}
