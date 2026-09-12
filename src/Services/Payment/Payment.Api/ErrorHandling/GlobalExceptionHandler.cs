using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Payment.Api.Exceptions;

namespace Payment.Api.ErrorHandling;

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
            UnknownPaymentMethodException => (StatusCodes.Status400BadRequest, "Unknown payment method"),

            // Almost certainly the unique index on OrderId firing. PaymentService
            // checks for an existing payment first, so reaching here means two
            // requests raced through that check together - and the index caught
            // the second one, which is exactly its job.
            //
            // 409 is the honest answer: the order IS charged, just not by this
            // request. The caller should re-read the payment rather than retry
            // the charge. And note the alternative - letting this become a 500 -
            // would tell a client "something broke, try again", which in a
            // payment system is the worst possible advice.
            DbUpdateException => (StatusCodes.Status409Conflict, "This order is already being paid for"),

            _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred")
        };

        var isExpected = exception is PaymentApplicationException or DbUpdateException;

        if (isExpected)
        {
            _logger.LogWarning(
                "Payment rule rejected {Method} {Path}: {Message}",
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
