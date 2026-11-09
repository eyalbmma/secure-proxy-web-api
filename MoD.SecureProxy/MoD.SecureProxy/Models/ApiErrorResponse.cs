namespace MoD.SecureProxy.Models;

public sealed record ApiErrorResponse(
    string Code,
    string Message,
    string CorrelationId);
