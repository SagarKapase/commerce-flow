using Inventory.Application.Exceptions;
using Inventory.Domain.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Api.ErrorHandling;

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
        // Note this switch maps exceptions from TWO layers - Domain
        // (InsufficientStock, InvalidReservationState) and Application
        // (StockConcurrencyConflict, UnknownProduct). Neither layer knows what
        // an HTTP status code is; this file is the only place that does.
        var (statusCode, title) = exception switch
        {
            // The request is fine, the world just is not in a state that allows
            // it. All three are 409.
            InsufficientStockException => (StatusCodes.Status409Conflict, "Insufficient stock"),
            InvalidReservationStateException => (StatusCodes.Status409Conflict, "Invalid reservation state"),
            StockConcurrencyConflictException => (StatusCodes.Status409Conflict, "Concurrent update"),

            // The body names a product with no stock record.
            UnknownProductException => (StatusCodes.Status400BadRequest, "Unknown product"),

            _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred")
        };

        var isExpected =
            exception is InventoryApplicationException
                or InsufficientStockException
                or InvalidReservationStateException;

        if (isExpected)
        {
            _logger.LogWarning(
                "Inventory rule rejected {Method} {Path}: {Reason}",
                httpContext.Request.Method,
                httpContext.Request.Path,
                title);
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

        if (exception is StockConcurrencyConflictException)
        {
            // A concurrency conflict is the one 409 here that is genuinely
            // worth retrying immediately - the request was valid and simply
            // lost a race. Telling the client so is more useful than making it
            // guess.
            httpContext.Response.Headers.RetryAfter = "1";
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
