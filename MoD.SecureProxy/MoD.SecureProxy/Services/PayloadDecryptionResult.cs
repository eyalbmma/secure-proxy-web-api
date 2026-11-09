namespace MoD.SecureProxy.Services;

public sealed record PayloadDecryptionResult(bool IsValid, byte[]? PlaintextJson)
{
    public static PayloadDecryptionResult Invalid() => new(false, null);
    public static PayloadDecryptionResult Success(byte[] plaintextJson) => new(true, plaintextJson);
}
