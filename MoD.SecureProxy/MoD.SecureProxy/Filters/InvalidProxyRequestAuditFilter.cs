using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using MoD.SecureProxy.Middleware;
using MoD.SecureProxy.Models;
using MoD.SecureProxy.Services;

namespace MoD.SecureProxy.Filters;

/// <summary>
/// Audits model-binding and validation rejections before ApiController's
/// automatic invalid-model-state filter can short-circuit the action.
/// Never records the request body or validation details.
/// </summary>
public sealed class InvalidProxyRequestAuditFilter : IAsyncActionFilter, IOrderedFilter
{
    private const int InvalidRequestStatusCode = StatusCodes.Status400BadRequest;
    private readonly RequestAuditor _requestAuditor;

    public InvalidProxyRequestAuditFilter(RequestAuditor requestAuditor)
    {
        _requestAuditor = requestAuditor;
    }

    // ApiController's automatic model-state filter uses order -2000.
    public int Order => -3000;

    public async Task OnActionExecutionAsync(
        ActionExecutingContext context,
        ActionExecutionDelegate next)
    {
        if (context.ModelState.IsValid)
        {
            await next();
            return;
        }

        var httpContext = context.HttpContext;
        var correlationId = httpContext.Items[CorrelationIdMiddleware.ContextItemKey] as string
            ?? httpContext.TraceIdentifier;

        var auditLog = await _requestAuditor.TryStartAsync(
            correlationId,
            "invalid_request",
            httpContext.RequestAborted);

        if (auditLog is null)
        {
            context.Result = Error(
                StatusCodes.Status503ServiceUnavailable,
                "audit_unavailable",
                "The request cannot be processed at this time.",
                correlationId);
            return;
        }

        await _requestAuditor.TryCompleteAsync(
            auditLog,
            InvalidRequestStatusCode,
            "Rejected",
            "InvalidRequestModel",
            forwardingOccurred: false);

        context.Result = Error(
            InvalidRequestStatusCode,
            "invalid_request",
            "The request body is invalid.",
            correlationId);
    }

    private static ObjectResult Error(int statusCode, string code, string message, string correlationId) =>
        new(new ApiErrorResponse(code, message, correlationId))
        {
            StatusCode = statusCode
        };
}
