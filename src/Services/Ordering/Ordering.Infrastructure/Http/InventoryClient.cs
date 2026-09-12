using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Ordering.Application.Abstractions;
using Ordering.Application.Exceptions;

namespace Ordering.Infrastructure.Http;

/// <summary>What Ordering sends to POST /api/inventory/reservations.</summary>
internal sealed record CreateReservationPayload(
    Guid ReferenceId,
    IReadOnlyList<ReservationLinePayload> Lines);

internal sealed record ReservationLinePayload(Guid ProductId, int Quantity);

/// <summary>
/// Inventory's reply. It also returns referenceId, status, lines and two
/// timestamps; we read the id and ignore the rest.
/// </summary>
internal sealed record ReservationCreatedResponse(Guid Id);

/// <summary>Inventory's ProblemDetails, for the 409 message.</summary>
internal sealed record ProblemDetailsResponse(string? Title, string? Detail);

public sealed class InventoryClient : IInventoryClient
{
    private const string ServiceName = "Inventory";

    private readonly HttpClient _httpClient;
    private readonly ILogger<InventoryClient> _logger;

    public InventoryClient(HttpClient httpClient, ILogger<InventoryClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<StockReservationResult> ReserveAsync(
        Guid orderId,
        IReadOnlyCollection<StockReservationLine> lines,
        CancellationToken cancellationToken)
    {
        // The order id becomes Inventory's ReferenceId, so every hold can be
        // traced back to what it is for - and so Phase 13 has a key to
        // deduplicate on.
        var payload = new CreateReservationPayload(
            orderId,
            lines.Select(line => new ReservationLinePayload(line.ProductId, line.Quantity)).ToList());

        try
        {
            using var response = await _httpClient.PostAsJsonAsync(
                "api/inventory/reservations", payload, cancellationToken);

            // ---------------------------------------------------------
            // THREE OUTCOMES, AND THE CODE BELOW KEEPS THEM APART.
            // ---------------------------------------------------------

            if (response.StatusCode == HttpStatusCode.Created)
            {
                var created = await response.Content
                    .ReadFromJsonAsync<ReservationCreatedResponse>(cancellationToken);

                if (created is null || created.Id == Guid.Empty)
                {
                    // A 201 with no usable id. We cannot say the stock is held
                    // and we cannot say it is not - so it is the third outcome,
                    // not a refusal.
                    throw new DownstreamUnavailableException(
                        ServiceName, "The inventory service returned an unusable reservation.");
                }

                return StockReservationResult.Succeeded(created.Id);
            }

            // 409 - Inventory ANSWERED, and the answer is no. Not enough stock,
            // or a concurrent request won the last unit. This is a business
            // outcome the customer is entitled to see, so it becomes a Result
            // rather than an exception, and the message comes straight from
            // Inventory ("only 2 available") rather than being invented here.
            if (response.StatusCode == HttpStatusCode.Conflict)
            {
                var problem = await ReadProblemAsync(response, cancellationToken);

                return StockReservationResult.Refused(
                    problem ?? "The requested items are not available in the quantity ordered.");
            }

            // 400 - a product with no stock record at all. From the customer's
            // side this is indistinguishable from being out of stock, and it is
            // certainly not their fault, so it is also a refusal rather than an
            // error thrown in their face.
            if (response.StatusCode == HttpStatusCode.BadRequest)
            {
                var problem = await ReadProblemAsync(response, cancellationToken);

                _logger.LogWarning(
                    "Inventory rejected the reservation for order {OrderId} as a bad request: {Problem}",
                    orderId,
                    problem);

                return StockReservationResult.Refused(
                    problem ?? "One or more items in this order are not currently stocked.");
            }

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                throw new DownstreamUnavailableException(
                    ServiceName, "The inventory service rejected this request's credentials.");
            }

            throw new DownstreamUnavailableException(
                ServiceName, $"The inventory service responded with {(int)response.StatusCode}.");
        }
        catch (HttpRequestException exception)
        {
            throw Unavailable("could not be reached", exception);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            // The most dangerous branch in this file. A timeout does NOT mean
            // the reservation did not happen - Inventory may have processed it
            // perfectly and lost the response on the way back. We genuinely do
            // not know, which is why Phase 13 makes this call safe to repeat.
            throw Unavailable("did not respond in time", exception);
        }
    }

    public Task ConfirmAsync(Guid reservationId, CancellationToken cancellationToken) =>
        PostReservationActionAsync(reservationId, "confirm", cancellationToken);

    public Task ReleaseAsync(Guid reservationId, CancellationToken cancellationToken) =>
        PostReservationActionAsync(reservationId, "release", cancellationToken);

    /// <summary>
    /// Confirm and release differ only by one word in the URL, so they share
    /// one method. Both are idempotent on Inventory's side, so both are safe
    /// to retry - which is the property the saga in Phase 11 will depend on.
    /// </summary>
    private async Task PostReservationActionAsync(
        Guid reservationId,
        string action,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient.PostAsync(
                $"api/inventory/reservations/{reservationId}/{action}",
                content: null,
                cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return;
            }

            // A 409 here means the reservation is in a state that forbids this -
            // releasing something already confirmed, say. That is a genuine
            // inconsistency between two services and it deserves a loud log,
            // because no automatic recovery is possible: only a human can
            // decide whether the goods shipped.
            var problem = await ReadProblemAsync(response, cancellationToken);

            _logger.LogError(
                "Inventory refused to {Action} reservation {ReservationId}: {StatusCode} {Problem}",
                action,
                reservationId,
                (int)response.StatusCode,
                problem);

            throw new DownstreamUnavailableException(
                ServiceName,
                $"The inventory service could not {action} the reservation for this order.");
        }
        catch (HttpRequestException exception)
        {
            throw Unavailable($"could not be reached to {action} a reservation", exception);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw Unavailable($"did not respond in time when asked to {action} a reservation", exception);
        }
    }

    private static async Task<string?> ReadProblemAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            var problem = await response.Content
                .ReadFromJsonAsync<ProblemDetailsResponse>(cancellationToken);

            return problem?.Detail ?? problem?.Title;
        }
        catch
        {
            // The body was not ProblemDetails. Not worth failing the whole
            // request over - the caller has a sensible default message.
            return null;
        }
    }

    private DownstreamUnavailableException Unavailable(string what, Exception inner)
    {
        _logger.LogError(
            inner,
            "The inventory service at {BaseAddress} {What}",
            _httpClient.BaseAddress,
            what);

        return new DownstreamUnavailableException(
            ServiceName, $"The inventory service {what}.", inner);
    }
}
