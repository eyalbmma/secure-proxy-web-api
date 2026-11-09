namespace MoD.SecureProxy.Security;

public sealed class EncryptionConfigurationException : Exception
{
    public EncryptionConfigurationException()
        : base("Encryption key configuration is unavailable or invalid.")
    {
    }
}
