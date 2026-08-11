using Microsoft.AspNetCore.Diagnostics;

namespace OrderProcessing.Api.Middleware;

/// <summary>
/// Maps Domain validation/state errors to ProblemDetails instead of letting them surface as raw
/// 500s. ArgumentException means the request was malformed; InvalidOperationException means the
/// request was well-formed but not allowed for the Order's current state (e.g. cancel a Completed order).
/// </summary>
public sealed class DomainExceptionHandler(IProblemDetailsService problemDetailsService, ILogger<DomainExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (statusCode, title) = exception switch
        {
            ArgumentException => (StatusCodes.Status400BadRequest, "Invalid request."),
            InvalidOperationException => (StatusCodes.Status409Conflict, "Operation not allowed for the current order state."),
            _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred.")
        };

        if (statusCode == StatusCodes.Status500InternalServerError)
            logger.LogError(exception, "Unhandled exception processing {Path}", httpContext.Request.Path);

        httpContext.Response.StatusCode = statusCode;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails =
            {
                Title = title,
                Detail = exception.Message,
                Status = statusCode
            }
        });
    }
}
