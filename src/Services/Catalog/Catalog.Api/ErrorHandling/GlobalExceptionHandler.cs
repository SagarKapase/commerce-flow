using Catalog.Application.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Catalog.Api.ErrorHandling;

/// <summary>
/// Turns exceptions into HTTP responses in one place.
///
/// WHY THIS EXISTS:
/// Without it, every controller action would need a try/catch to convert a
/// DuplicateSkuException into a 409 - the same six lines repeated in every
/// method, and forgotten in the one that matters.
///
/// WHY IT LIVES IN THE API PROJECT:
/// This is the only file in the solution that knows a DuplicateSkuException
/// means "409". The application layer throws domain-flavoured exceptions and
/// stays unaware that HTTP exists; the translation happens at the edge.
///
/// IExceptionHandler is the modern (.NET 8+) replacement for hand-written
/// try/catch middleware. Handlers are called in registration order until one
/// returns true.
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
            // 409 Conflict: the request is well-formed and the caller is
            // allowed to make it, but it collides with the current state.
            DuplicateSkuException => (StatusCodes.Status409Conflict, "Duplicate SKU"),
            DuplicateCategorySlugException => (StatusCodes.Status409Conflict, "Duplicate category slug"),
            CategoryHasActiveProductsException => (StatusCodes.Status409Conflict, "Category is not empty"),

            // 400 Bad Request: the body itself is wrong - it names a category
            // that does not exist.
            ReferencedCategoryNotFoundException => (StatusCodes.Status400BadRequest, "Unknown category"),

            // Anything else is a bug we have not accounted for.
            _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred")
        };

        // Log level carries meaning. A rejected duplicate SKU is the system
        // working correctly and should not page anyone at 3am; an unhandled
        // exception is a defect. Passing the exception as the FIRST argument
        // (not $"{ex}") preserves the stack trace for structured log sinks.
        if (exception is CatalogApplicationException)
        {
            _logger.LogWarning(
                exception,
                "Business rule rejected {Method} {Path}",
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

        return await _problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = statusCode,
                Title = title,

                // Business messages are safe and useful to return - they were
                // written for the caller. An unexpected exception's message may
                // contain SQL, file paths or connection details, so it is only
                // revealed in Development. In Production the caller gets the
                // traceId and we get the details in the log.
                Detail = exception is CatalogApplicationException || _environment.IsDevelopment()
                    ? exception.Message
                    : null
            }
        });
    }
}
