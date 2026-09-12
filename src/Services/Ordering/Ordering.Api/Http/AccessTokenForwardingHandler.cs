using System.Net.Http.Headers;

namespace Ordering.Api.Http;

/// <summary>
/// Copies the caller's bearer token onto every outbound request.
///
/// ============ WHY ORDERING MUST FORWARD THE TOKEN ============
/// Basket's GET /api/basket does not take a user id. It reads the "sub" claim
/// off the token and returns THAT person's basket - the design decision from
/// Phase 4 that made it impossible to ask for somebody else's.
///
/// Which means Ordering cannot ask "give me Alice's basket". It can only say
/// "give me the basket of whoever this token belongs to", and to do that it has
/// to present Alice's own token. The protection Basket built survives the
/// service boundary instead of being lost at it.
/// =============================================================
///
/// ============ WHAT A DelegatingHandler IS ============
/// IHttpClientFactory builds a PIPELINE of message handlers, exactly like
/// ASP.NET Core's middleware but for outgoing calls. Each handler sees the
/// request on the way out and the response on the way back, and decides whether
/// to pass it along.
///
///   HttpClient -> [AccessTokenForwarding] -> [logging] -> [primary handler] -> network
///
/// Doing this here rather than in each client means CatalogClient,
/// BasketClient and InventoryClient never mention authentication at all, and a
/// new client added next year cannot forget to attach the token. Cross-cutting
/// concerns belong in the pipeline, not in every method.
///
/// It is also where retries, circuit breakers and correlation-id propagation
/// go - which is what Phase 17 will add, in exactly this place.
/// =====================================================
///
/// This class lives in Ordering.Api, not Ordering.Infrastructure, because it
/// reads the INCOMING request through IHttpContextAccessor. Incoming requests
/// are a hosting concern; the clients in Infrastructure only make outgoing
/// ones and need no reference to ASP.NET Core.
/// </summary>
public sealed class AccessTokenForwardingHandler : DelegatingHandler
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public AccessTokenForwardingHandler(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        // Null when there is no request in flight - a background job, a health
        // check, a unit test. Nothing to forward, so we forward nothing and let
        // the downstream service reject it with a 401. Silently inventing a
        // token here would be far worse than an honest failure.
        var incoming = _httpContextAccessor.HttpContext?.Request.Headers.Authorization.ToString();

        if (!string.IsNullOrWhiteSpace(incoming) &&
            AuthenticationHeaderValue.TryParse(incoming, out var header) &&
            header.Scheme.Equals("Bearer", StringComparison.OrdinalIgnoreCase))
        {
            request.Headers.Authorization = header;
        }

        return base.SendAsync(request, cancellationToken);
    }
}
