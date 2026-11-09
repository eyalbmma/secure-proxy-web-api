namespace MoD.SecureProxy.Services;

public sealed record RequestValidationResult(
    bool IsValid,
    bool IsConfigurationIssue,
    string? ErrorCode = null,
    byte[]? BodyJson = null,
    IReadOnlyDictionary<string, string>? PathParameters = null,
    IReadOnlyDictionary<string, string>? QueryParameters = null);
