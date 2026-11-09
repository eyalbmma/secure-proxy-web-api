namespace MoD.SecureProxy.Services;

public sealed record RequestValidationResult(
    bool IsValid,
    bool IsConfigurationIssue,
    string? ErrorCode = null);
