using System.Net;
using System.Net.Http.Json;
using Basket.Api.Exceptions;

namespace Basket.Api.Catalog;

/// <summary>
/// Talks to Catalog over HTTP.
///
/// This is a TYPED CLIENT. It receives an HttpClient through its constructor,
/// already configured with a base address and a timeout by the registration in
/// Program.cs. It does not create one, does not know the URL, and does not
/// dispose it - the factory owns the lifetime.
/// </summary>
public sealed class CatalogClient : ICatalogClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<CatalogClient> _logger;

    public CatalogClient(HttpClient httpClient, ILogger<CatalogClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<CatalogProduct?> GetProductAsync(
        Guid productId,
        CancellationToken cancellationToken)
    {
        // NO LEADING SLASH. "api/products/x" appends to the base address;
        // "/api/products/x" would reset to the ROOT of the host and silently
        // throw away any path in BaseAddress. It works fine here - our base is
        // just a host - and breaks the day the service moves behind a gateway
        // at /catalog/. Two characters, one very confusing afternoon.
        var requestUri = $"api/products/{productId}";

        try
        {
            using var response = await _httpClient.GetAsync(requestUri, cancellationToken);

            // A 404 is Catalog ANSWERING. The product genuinely does not exist,
            // and the caller needs to hear that - so it is null, not an
            // exception. Exceptions are for questions we could not ask.
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                _logger.LogInformation("Catalog reports product {ProductId} does not exist", productId);

                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                // 500 from Catalog, or 401, or anything else unexpected. From
                // our side these are all the same thing: Catalog is not working
                // properly right now. Note we do NOT pass Catalog's status code
                // through to our own caller - a 500 in Catalog is not a 500 in
                // Basket's API contract.
                throw new CatalogUnavailableException(
                    $"The catalog service responded with {(int)response.StatusCode}.");
            }

            var product = await response.Content
                .ReadFromJsonAsync<CatalogProduct>(cancellationToken);

            if (product is null)
            {
                // 200 with a body of literal "null". Rare, but a nullable
                // deserialisation result the compiler is warning about is not
                // something to silence with a `!`.
                throw new CatalogUnavailableException(
                    "The catalog service returned an empty response body.");
            }

            return product;
        }
        catch (HttpRequestException exception)
        {
            // Connection refused, DNS failure, TLS handshake failure - we never
            // got as far as an HTTP response. The most common one in practice
            // is simply "that service is not running".
            _logger.LogError(
                exception,
                "Could not reach the catalog service at {BaseAddress}",
                _httpClient.BaseAddress);

            throw new CatalogUnavailableException(
                "The catalog service could not be reached.", exception);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            // ---------------------------------------------------------------
            // THE `when` CLAUSE IS NOT DECORATION.
            //
            // HttpClient throws TaskCanceledException for TWO completely
            // different events:
            //   1. Our own Timeout elapsed - Catalog is too slow. Our problem
            //      to report, and worth an alert.
            //   2. The caller cancelled - the customer closed the browser. That
            //      is normal, nobody is waiting for an answer, and logging it as
            //      an error would fill the logs with noise every time someone
            //      navigates away.
            //
            // Checking the token tells them apart. Without the filter, every
            // abandoned request looks like a Catalog outage.
            // (.NET 5+ also puts a TimeoutException in InnerException on the
            // timeout path, which is a second way to make the same distinction.)
            // ---------------------------------------------------------------
            _logger.LogError(
                exception,
                "The catalog service did not respond within {Timeout}",
                _httpClient.Timeout);

            throw new CatalogUnavailableException(
                "The catalog service did not respond in time.", exception);
        }
    }
}
