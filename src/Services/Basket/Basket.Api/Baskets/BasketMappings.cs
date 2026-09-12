using Basket.Api.Domain;
using Basket.Api.Dtos;

namespace Basket.Api.Baskets;

internal static class BasketMappings
{
    public static BasketResponse ToResponse(CustomerBasket basket)
    {
        return new BasketResponse(
            basket.UserId,
            basket.Items
                .OrderBy(item => item.ProductName)
                .Select(item => new BasketItemResponse(
                    item.ProductId,
                    item.ProductName,
                    item.UnitPrice,
                    item.Quantity,
                    item.LineTotal))
                .ToList(),
            basket.TotalQuantity,
            basket.TotalAmount,
            basket.UpdatedAtUtc);
    }

    /// <summary>
    /// The response for a user who has never added anything.
    ///
    /// We return an empty basket rather than a 404. A basket is not a resource
    /// you create - every signed-in user conceptually has one, it just might be
    /// empty. Making the client handle "404 means empty" is how you end up with
    /// a null-check bug on somebody's first visit.
    ///
    /// Note the database has no row at this point, and does not need one.
    /// </summary>
    public static BasketResponse EmptyFor(Guid userId)
    {
        return new BasketResponse(userId, Array.Empty<BasketItemResponse>(), 0, 0m, null);
    }
}
