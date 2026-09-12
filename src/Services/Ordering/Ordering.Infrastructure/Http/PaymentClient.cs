using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Ordering.Application.Abstractions;
using Ordering.Application.Exceptions;

namespace Ordering.Infrastructure.Http;

internal sealed record CreatePaymentPayload(
    Guid PaymentId,
    Guid OrderId,
    Guid CustomerId,
    decimal Amount,
    string PaymentMethodToken);

/// <summary>
/// Payment's reply. It also returns the amount, the provider reference and two
/// timestamps; we read what we act on.
/// </summary>
internal sealed record PaymentCreatedResponse(
    Guid Id,
    string Status,
    string? FailureReason);

public sealed class PaymentClient : IPaymentClient
{
    private const string ServiceName = "Payment";

    private readonly HttpClient _httpClient;
    private readonly ILogger<PaymentClient> _logger;

    public PaymentClient(HttpClient httpClient, ILogger<PaymentClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<PaymentResult> ChargeAsync(
        Guid paymentId,
        Guid orderId,
        Guid customerId,
        decimal amount,
        string paymentMethodToken,
        CancellationToken cancellationToken)
    {
        // The amount comes from the ORDER, computed from the basket, priced by
        // Catalog. The customer never states it - they only supply the token
        // identifying the card, which passes through us untouched.
        // The payment id goes OUT with the request rather than coming back in
        // the response. That is the whole point: we already saved it, so the
        // order and the payment agree on the identifier even if this call's
        // reply is lost.
        var payload = new CreatePaymentPayload(
            paymentId, orderId, customerId, amount, paymentMethodToken);

        try
        {
            using var response = await _httpClient.PostAsJsonAsync(
                "api/payments", payload, cancellationToken);

            if (response.StatusCode == HttpStatusCode.Created)
            {
                var payment = await response.Content
                    .ReadFromJsonAsync<PaymentCreatedResponse>(cancellationToken);

                if (payment is null || payment.Id == Guid.Empty)
                {
                    throw new DownstreamUnavailableException(
                        ServiceName, "The payment service returned an unusable response.");
                }

                // A 201 means the REQUEST worked. Whether the CARD worked is in
                // the body - which is exactly the distinction Payment's
                // controller documents, seen from the other side.
                return payment.Status.Equals("Succeeded", StringComparison.OrdinalIgnoreCase)
                    ? PaymentResult.Charged(payment.Id)
                    : PaymentResult.Declined(
                        payment.Id,
                        payment.FailureReason ?? "The payment was declined.");
            }

            if (response.StatusCode == HttpStatusCode.BadRequest)
            {
                // An unrecognised payment method token. The customer chose it,
                // so it is their problem to fix, not an outage.
                var problem = await response.Content
                    .ReadFromJsonAsync<ProblemDetailsResponse>(cancellationToken);

                throw new PaymentRejectedException(
                    problem?.Detail ?? "That payment method is not accepted.");
            }

            throw new DownstreamUnavailableException(
                ServiceName, $"The payment service responded with {(int)response.StatusCode}.");
        }
        catch (HttpRequestException exception)
        {
            throw Unavailable("could not be reached", exception);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            // ---------------------------------------------------------------
            // THE WORST BRANCH IN THE WHOLE SYSTEM.
            //
            // We waited, and gave up. The card may have been charged anyway.
            // We have no way to find out from here, and no safe automatic
            // action: releasing the stock risks a paid order with nothing
            // reserved, and doing nothing leaves the order stuck.
            //
            // Reproduce it deliberately with tok_timeout and watch what
            // happens to the order. That is the argument for the saga.
            // ---------------------------------------------------------------
            throw Unavailable("did not respond in time", exception);
        }
    }

    private DownstreamUnavailableException Unavailable(string what, Exception inner)
    {
        _logger.LogError(
            inner,
            "The payment service at {BaseAddress} {What}. THE CHARGE MAY STILL HAVE COMPLETED",
            _httpClient.BaseAddress,
            what);

        return new DownstreamUnavailableException(
            ServiceName, $"The payment service {what}.", inner);
    }
}
