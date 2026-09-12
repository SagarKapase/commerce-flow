using System.Globalization;
using System.Threading.RateLimiting;
using CommerceFlow.BuildingBlocks.Authentication;

// =====================================================================
// THE API GATEWAY
//
// One door into a system of six services. Everything in this file is a
// cross-cutting EDGE concern - true of every request, whichever service
// eventually serves it. There is no business logic, and there is no
// Controllers folder to put any in.
//
// The routing table lives entirely in appsettings.json. Read that file
// first: what it does NOT contain is the point of this phase.
// =====================================================================

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------
// 1. YARP
//
// LoadFromConfig means routes and clusters are configuration, not code.
// Adding a service, moving one to a new host, or load-balancing across two
// instances is an appsettings edit - and in production, a config reload
// with no redeploy at all, because YARP watches the source and swaps the
// routing table live.
// ---------------------------------------------------------------------
var proxyBuilder = builder.Services
    .AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

if (builder.Environment.IsDevelopment())
{
    // Development only. The six services present the self-signed ASP.NET Core
    // development certificate, so unless it has been trusted on this machine
    // (`dotnet dev-certs https --trust`) the TLS handshake fails before the
    // request is forwarded. Same exception, same reasoning, as the typed
    // clients in Basket and Ordering - and equally a serious finding anywhere
    // but a developer's laptop.
    proxyBuilder.ConfigureHttpClient((_, handler) =>
    {
        handler.SslOptions.RemoteCertificateValidationCallback = (_, _, _, _) => true;
    });
}

// ---------------------------------------------------------------------
// 2. Authentication at the edge
//
// The same shared BuildingBlock every service uses, so the gateway agrees
// with them about issuer, audience, signing key and claim names by
// construction rather than by coincidence.
//
// This is DEFENCE IN DEPTH, not a replacement. Routes marked "default"
// get their obviously-anonymous traffic rejected here, one hop earlier and
// without waking a service - but each service still validates the token
// itself, because a gateway you have to trust completely is a gateway
// whose compromise is total.
// ---------------------------------------------------------------------
builder.Services.AddCommerceFlowJwtAuthentication(builder.Configuration);

// ---------------------------------------------------------------------
// 3. Rate limiting
//
// The clearest answer to "why have a gateway at all, if it only forwards?"
//
// Protecting six services from abuse means either six copies of this
// policy - which will drift, and which one of them will be missing - or
// one copy at the door. Cross-cutting concerns belong where the traffic
// converges.
// ---------------------------------------------------------------------
var permitLimit = builder.Configuration.GetValue("RateLimiting:PermitLimit", 100);
var windowSeconds = builder.Configuration.GetValue("RateLimiting:WindowSeconds", 60);

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
    {
        // PARTITION KEY: the signed-in user if we know them, otherwise the
        // client's IP address.
        //
        // Why prefer the user: an office, a university or a mobile carrier can
        // put thousands of people behind one address. Limiting purely by IP
        // means one heavy user can lock out everybody who shares it, and a
        // determined attacker just changes address.
        //
        // Why fall back to IP: sign-in and product browsing happen before there
        // is a user to key on, and those are exactly the endpoints worth
        // protecting from a credential-stuffing script.
        var partitionKey =
            httpContext.User.FindFirst(JwtClaimNames.Sub)?.Value
            ?? httpContext.Connection.RemoteIpAddress?.ToString()
            ?? "unknown";

        // A FIXED window: a simple counter that resets on the minute. Easy to
        // reason about, and it has a known flaw worth being able to name - a
        // caller can send the full allowance at 11:59:59 and again at 12:00:00,
        // so the real short-term burst is double the limit. A sliding window or
        // a token bucket smooths that out at the cost of more state; both are
        // one line away in AddRateLimiter.
        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ =>
            new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = TimeSpan.FromSeconds(windowSeconds),

                // Queue nothing. Making a caller who is already over the limit
                // WAIT would hold a connection open and turn a rate limit into
                // a slow-request attack on ourselves. Refuse immediately and
                // tell them when to come back.
                QueueLimit = 0
            });
    });

    options.OnRejected = async (context, cancellationToken) =>
    {
        // Retry-After turns "you are going too fast" into something a client
        // library can obey automatically, instead of a message a human might
        // read. Without it, a well-meaning retry loop hammers the door harder
        // the more it is refused.
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            context.HttpContext.Response.Headers.RetryAfter =
                ((int)retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
        }
        else
        {
            context.HttpContext.Response.Headers.RetryAfter =
                windowSeconds.ToString(CultureInfo.InvariantCulture);
        }

        context.HttpContext.Response.ContentType = "application/problem+json";

        await context.HttpContext.Response.WriteAsync(
            $$"""
            {"type":"https://tools.ietf.org/html/rfc9110#section-15.5.29","title":"Too many requests","status":429,"detail":"You have exceeded {{permitLimit}} requests per {{windowSeconds}} seconds. Try again shortly."}
            """,
            cancellationToken);
    };
});

var app = builder.Build();

// =====================================================================
// MIDDLEWARE PIPELINE
// =====================================================================

app.UseHttpsRedirection();

// ---------------------------------------------------------------------
// The UI, served from wwwroot.
//
// WHY THE GATEWAY SERVES IT: the browser then loads the page from
// https://localhost:7100 and calls https://localhost:7100/api/... - the
// SAME ORIGIN. That means no CORS anywhere in the system: not one
// AddCors(), not one policy, not one preflight.
//
// Serve the page from anywhere else and every service would need to allow
// that origin, and every service would need changing when it moved. Six
// CORS policies to keep in step, or one static file. This is a real and
// underrated reason gateways exist.
//
// BEFORE the authentication and rate-limiting middleware, deliberately:
// the page and its CSS must load for somebody who is not signed in (they
// have to reach the sign-in form), and a page load pulling three files
// should not spend three of the caller's rate-limit allowance.
// ---------------------------------------------------------------------
app.UseDefaultFiles();
app.UseStaticFiles();

// Authentication first, so the rate limiter can partition by user rather
// than by address.
//
// The trade-off is real and worth stating: a request now costs a signature
// verification BEFORE it can be throttled, so a flood still does some work.
// A production edge puts a cheap IP-based limit in front of this - at a CDN,
// a WAF, or a load balancer - and keeps the per-user limit here for fairness.
// Two layers, two different jobs.
app.UseAuthentication();
app.UseAuthorization();

app.UseRateLimiter();

// The whole gateway, in one line. Routes and clusters come from
// configuration; YARP matches, forwards, streams the response back, and
// adds X-Forwarded-For, X-Forwarded-Proto and X-Forwarded-Host so the
// service behind it can still tell who the original caller was.
app.MapReverseProxy();

// A request that matches NO route gets a 404 from here, and never touches a
// service. That is how /api/payments is closed: not by a check, but by the
// absence of anywhere for it to go.
app.Run();
