using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Ordering.Application.Abstractions;
using Ordering.Application.Exceptions;

namespace Ordering.Infrastructure.Http;

/// <summary>
/// The wire shape of Basket's response, as far as Ordering cares.
///
/// Basket returns userId, items, totalQuantity, totalAmount and updatedAtUtc;
/// each item also carries a lineTotal. Ordering reads the items and ignores
/// everything else - a tolerant reader, so Basket can add fields freely.
/// </summary>
internal sealed record BasketSnapshotResponse(IReadOnlyList<BasketLineSnapshot> Items);

public sealed class BasketClient : IBasketClient
{
    private const string ServiceName = "Basket";

    private readonly HttpClient _httpClient;
    private readonly ILogger<BasketClient> _logger;

    public BasketClient(HttpClient httpClient, ILogger<BasketClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<IReadOnlyList<BasketLineSnapshot>> GetCurrentBasketAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient.GetAsync("api/basket", cancellationToken);

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                // Our forwarded token was rejected. Almost always a
                // configuration problem - a signing key or issuer that does not
                // match - and it deserves its own message, because "Basket is
                // unavailable" would send somebody looking at the wrong thing
                // entirely.
                throw new DownstreamUnavailableException(
                    ServiceName,
                    "The basket service rejected this request's credentials.");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new DownstreamUnavailableException(
                    ServiceName,
                    $"The basket service responded with {(int)response.StatusCode}.");
            }

            var basket = await response.Content
                .ReadFromJsonAsync<BasketSnapshotResponse>(cancellationToken);

            // Basket returns an empty basket rather than a 404 for a customer
            // who has never added anything - so an empty list here is a normal
            // answer, not a failure. OrderService turns it into a 400.
            return basket?.Items ?? [];
        }
        catch (HttpRequestException exception)
        {
            throw Unavailable("could not be reached", exception);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Our timeout, not the caller hanging up. The `when` clause is what
            // tells those two apart - see CatalogClient in the Basket service
            // for the long version.
            throw Unavailable("did not respond in time", exception);
        }
    }

    public async Task ClearCurrentBasketAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient.DeleteAsync("api/basket", cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw new DownstreamUnavailableException(
                    ServiceName,
                    $"The basket service responded with {(int)response.StatusCode} when clearing the basket.");
            }
        }
        catch (HttpRequestException exception)
        {
            throw Unavailable("could not be reached", exception);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw Unavailable("did not respond in time", exception);
        }
    }

    private DownstreamUnavailableException Unavailable(string what, Exception inner)
    {
        _logger.LogError(
            inner,
            "The basket service at {BaseAddress} {What}",
            _httpClient.BaseAddress,
            what);

        return new DownstreamUnavailableException(
            ServiceName, $"The basket service {what}.", inner);
    }
}
