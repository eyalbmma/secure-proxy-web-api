using MoD.SecureProxy.Models;

namespace MoD.SecureProxy.Middleware;

public sealed class ErrorHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ErrorHandlingMiddleware> _logger;

    public ErrorHandlingMiddleware(
        RequestDelegate next,
        ILogger<ErrorHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // The caller disconnected. Do not attempt to write an HTTP error response.
            _logger.LogInformation(
                "Request cancelled by caller. CorrelationId: {CorrelationId}",
                GetCorrelationId(context));
        }
        catch (Exception exception)
        {
            var correlationId = GetCorrelationId(context);

            // Log only the exception type here. Exception messages can contain URLs or configuration details.
            _logger.LogError(
                "Unhandled request exception. CorrelationId: {CorrelationId}, ExceptionType: {ExceptionType}",
                correlationId,
                exception.GetType().Name);

            if (context.Response.HasStarted)
            {
                throw;
            }

            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/json";

            await context.Response.WriteAsJsonAsync(new ApiErrorResponse(
                "internal_error",
                "An unexpected error occurred.",
                correlationId));
        }
    }

    private static string GetCorrelationId(HttpContext context) =>
        context.Items[CorrelationIdMiddleware.ContextItemKey] as string
        ?? context.TraceIdentifier;
}
