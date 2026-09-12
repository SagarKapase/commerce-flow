using Basket.Api.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Basket.Api.ErrorHandling;

/// <summary>
/// Maps Basket's application exceptions onto HTTP status codes.
///
/// Third copy of this pattern. The plumbing is identical across all three
/// services; the switch below is not, and never will be. That is exactly why
/// this has not moved to BuildingBlocks - what looks duplicated is the boring
/// half, and extracting it would leave every service with a base class it has
/// to override anyway.
/// </summary>
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
            BasketLimitExceededException => (StatusCodes.Status409Conflict, "Basket limit reached"),

            // The product is wrong, so the request body is wrong.
            ProductNotAvailableException => (StatusCodes.Status400BadRequest, "Product not available"),

            // We are fine; something we depend on is not. Not our bug, and not
            // a 500 - see CatalogUnavailableException for why the code matters.
            CatalogUnavailableException => (StatusCodes.Status503ServiceUnavailable, "Catalog service unavailable"),

            _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred")
        };

        // Log LEVEL is a separate decision from status code, and the two do not
        // always move together.
        if (exception is CatalogUnavailableException unavailable)
        {
            // Warning would be wrong here. A dependency being down is not
            // business-as-usual just because it is not our code at fault -
            // somebody needs to know a service is unreachable. The inner
            // exception carries the socket or timeout detail, which belongs in
            // the log and never in the response.
            _logger.LogError(
                unavailable.InnerFailure ?? unavailable,
                "Downstream dependency unavailable while processing {Method} {Path}",
                httpContext.Request.Method,
                httpContext.Request.Path);
        }
        else if (exception is BasketApplicationException)
        {
            _logger.LogWarning(
                "Basket rule rejected {Method} {Path}",
                httpContext.Request.Method,
                httpContext.Request.Path);
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
            // Retry-After turns "try again later" from advice in a message
            // nobody parses into a machine-readable instruction. A well-behaved
            // client - and every sensible HTTP library - will honour it instead
            // of hammering a service that is already struggling.
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
                Detail = exception is BasketApplicationException || _environment.IsDevelopment()
                    ? exception.Message
                    : null
            }
        });
    }
}
