using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;

namespace MoD.SecureProxy.Security;

/// <summary>
/// Local assessment key source. Key values belong in User Secrets or environment configuration,
/// never in the database or checked-in appsettings files.
/// </summary>
public sealed class ConfigurationKeySource : IKeySource
{
    private static readonly Regex ValidReference = new("^[A-Za-z0-9_-]{1,100}$", RegexOptions.Compiled);
    private readonly IConfiguration _configuration;

    public ConfigurationKeySource(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public Task<byte[]> GetKeyAsync(string keyReference, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(keyReference) || !ValidReference.IsMatch(keyReference))
        {
            throw new EncryptionConfigurationException();
        }

        var encodedKey = _configuration[$"EncryptionKeys:{keyReference}"];
        if (string.IsNullOrWhiteSpace(encodedKey))
        {
            throw new EncryptionConfigurationException();
        }

        byte[] key;
        try
        {
            key = Convert.FromBase64String(encodedKey);
        }
        catch (FormatException)
        {
            throw new EncryptionConfigurationException();
        }

        if (key.Length is not (16 or 24 or 32))
        {
            CryptographicOperations.ZeroMemory(key);
            throw new EncryptionConfigurationException();
        }

        return Task.FromResult(key);
    }
}
