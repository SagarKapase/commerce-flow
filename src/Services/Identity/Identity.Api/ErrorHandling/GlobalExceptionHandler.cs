using Identity.Application.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Identity.Api.ErrorHandling;

/// <summary>
/// Maps Identity's application exceptions onto HTTP status codes.
///
/// THIS IS THE SECOND COPY OF THIS PATTERN (Catalog has the first), and it is
/// duplicated on purpose. The shared part is about six lines of plumbing; the
/// part that matters - which exception means which status code - is different
/// in every service. Extracting it now would create a BuildingBlocks library
/// that every service must be redeployed for whenever one service needs a new
/// mapping. We will revisit when a third copy appears and we can see what is
/// genuinely common.
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
            EmailAlreadyRegisteredException => (StatusCodes.Status409Conflict, "Email already registered"),
            RegistrationFailedException => (StatusCodes.Status400BadRequest, "Registration failed"),

            // 401, not 403. The caller has not proved who they are.
            // 403 would mean "we know who you are, and you may not do this".
            InvalidCredentialsException => (StatusCodes.Status401Unauthorized, "Invalid credentials"),
            AccountLockedException => (StatusCodes.Status401Unauthorized, "Account locked"),
            InvalidRefreshTokenException => (StatusCodes.Status401Unauthorized, "Invalid refresh token"),

            _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred")
        };

        if (exception is IdentityApplicationException)
        {
            // Note: no email address, no password, no token in this message.
            // Logs get shipped, read and retained; credentials must never
            // reach them.
            _logger.LogWarning(
                "Authentication rule rejected {Method} {Path}: {Reason}",
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

        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = exception is IdentityApplicationException || _environment.IsDevelopment()
                ? exception.Message
                : null
        };

        // Password policy failures come back as a list so the caller can fix
        // every problem at once instead of one attempt at a time.
        if (exception is RegistrationFailedException registrationFailed)
        {
            problemDetails.Extensions["errors"] = registrationFailed.Errors;
        }

        return await _problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = problemDetails
        });
    }
}
