using ClaimsApi.Application;
using ClaimsApi.Domain;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace ClaimsApi.Api;

/// <summary>Maps domain and application exceptions to RFC 7807 problem responses in one place.</summary>
public class ApiExceptionHandler(IProblemDetailsService problems, ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        var (status, title) = exception switch
        {
            NotFoundException => (StatusCodes.Status404NotFound, "Resource not found"),
            DomainValidationException => (StatusCodes.Status400BadRequest, "Invalid request"),
            DomainException => (StatusCodes.Status409Conflict, "Business rule violation"),
            _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred")
        };

        if (status == StatusCodes.Status500InternalServerError)
            logger.LogError(exception, "Unhandled exception for {Path}", context.Request.Path);

        context.Response.StatusCode = status;
        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Title = title,
                // Only surface messages we authored; never leak internals for unexpected errors.
                Detail = status == StatusCodes.Status500InternalServerError ? null : exception.Message
            }
        });
    }
}

