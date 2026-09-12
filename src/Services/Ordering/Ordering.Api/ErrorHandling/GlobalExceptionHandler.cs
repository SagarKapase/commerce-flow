using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Ordering.Application.Exceptions;
using Ordering.Domain.Exceptions;

namespace Ordering.Api.ErrorHandling;

internal sealed class GlobalExceptionHandler : IExceptionHandler
{
    private readonly IProblemDetailsService _problemDetailsService;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(
        IProblemDetailsService problemDetailsService,
        IHostEnvironment environment,
        ILogger<GlobalExceptionHandler> logger)
    {
        _problemDetailsService = problemDetailsService;
        _environment = environment;
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var (statusCode, title) = exception switch
        {
            // The order exists and the caller may act on it - the order is
            // simply not in a state where this makes sense. That is 409, and
            // the message names both the current state and what was attempted.
            InvalidOrderStateTransitionException => (StatusCodes.Status409Conflict, "Invalid order state"),

            // Nothing to order. The request was fine, the customer just has not
            // chosen anything yet.
            EmptyBasketException => (StatusCodes.Status400BadRequest, "Basket is empty"),

            // The payment method itself was refused - not the card, the token.
            // The customer can fix this; it is not an outage.
            PaymentRejectedException => (StatusCodes.Status400BadRequest, "Payment method not accepted"),

            // A service we depend on is down. Not 500 - Ordering has no bug -
            // and 503 is the only code that tells the client retrying later is
            // sensible.
            DownstreamUnavailableException => (StatusCodes.Status503ServiceUnavailable, "A required service is unavailable"),

            _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred")
        };

        var isExpected = exception is InvalidOrderStateTransitionException or OrderingApplicationException;

        if (exception is DownstreamUnavailableException unavailable)
        {
            // Error, not Warning. A dependency being down is not
            // business-as-usual just because it is not our code at fault, and
            // the SERVICE NAME goes in the log while the response stays vague -
            // the customer does not need our topology, the engineer on call
            // needs it precisely.
            _logger.LogError(
                unavailable.InnerFailure ?? unavailable,
                "{ServiceName} was unavailable while processing {Method} {Path}",
                unavailable.ServiceName,
                httpContext.Request.Method,
                httpContext.Request.Path);
        }
        else if (isExpected)
        {
            _logger.LogWarning(
                "Order rule rejected {Method} {Path}: {Message}",
                httpContext.Request.Method,
                httpContext.Request.Path,
                exception.Message);
        }
        else
        {
            _logger.LogError(
                exception,
                "Unhandled exception while processing {Method} {Path}",
                httpContext.Request.Method,
                httpContext.Request.Path);
        }

        httpContext.Response.StatusCode = statusCode;

        if (statusCode == StatusCodes.Status503ServiceUnavailable)
        {
            // Machine-readable "try again later", so a well-behaved client
            // backs off instead of hammering a service already in trouble.
            httpContext.Response.Headers.RetryAfter = "5";
        }

        return await _problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = statusCode,
                Title = title,
                Detail = isExpected || _environment.IsDevelopment()
                    ? exception.Message
                    : null
            }
        });
    }
}
