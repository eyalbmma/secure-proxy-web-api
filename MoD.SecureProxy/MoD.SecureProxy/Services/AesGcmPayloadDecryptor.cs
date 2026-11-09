using System.Security.Cryptography;
using System.Text.Json;
using MoD.SecureProxy.Security;

namespace MoD.SecureProxy.Services;

/// <summary>
/// Decrypts the assumed envelope { nonce, ciphertext, tag }, with each field Base64 encoded.
/// The nonce is 12 bytes, the authentication tag is 16 bytes, and associated data is empty.
/// </summary>
public sealed class AesGcmPayloadDecryptor
{
    private const int NonceSizeBytes = 12;
    private const int TagSizeBytes = 16;

    private readonly IKeySource _keySource;

    public AesGcmPayloadDecryptor(IKeySource keySource)
    {
        _keySource = keySource;
    }

    public async Task<PayloadDecryptionResult> TryDecryptAsync(
        JsonElement envelope,
        string keyReference,
        CancellationToken cancellationToken)
    {
        byte[] nonce = [];
        byte[] ciphertext = [];
        byte[] tag = [];
        byte[]? key = null;
        byte[]? plaintext = null;
        var returnPlaintextToCaller = false;

        try
        {
            if (envelope.ValueKind != JsonValueKind.Object ||
                !TryGetBase64(envelope, "nonce", out nonce) ||
                !TryGetBase64(envelope, "ciphertext", out ciphertext) ||
                !TryGetBase64(envelope, "tag", out tag) ||
                nonce.Length != NonceSizeBytes ||
                tag.Length != TagSizeBytes)
            {
                return PayloadDecryptionResult.Invalid();
            }

            key = await _keySource.GetKeyAsync(keyReference, cancellationToken);
            if (key.Length is not (16 or 24 or 32))
            {
                throw new EncryptionConfigurationException();
            }

            var plaintextBytes = new byte[ciphertext.Length];
            plaintext = plaintextBytes;
            using var aesGcm = new AesGcm(key, TagSizeBytes);
            aesGcm.Decrypt(nonce, ciphertext, tag, plaintextBytes, Array.Empty<byte>());

            // Reject authenticated bytes that are not valid JSON before returning them to the caller.
            using (JsonDocument.Parse(plaintextBytes))
            {
            }

            returnPlaintextToCaller = true;
            return PayloadDecryptionResult.Success(plaintextBytes);
        }
        catch (CryptographicException)
        {
            return PayloadDecryptionResult.Invalid();
        }
        catch (JsonException)
        {
            return PayloadDecryptionResult.Invalid();
        }
        finally
        {
            if (key is not null)
            {
                CryptographicOperations.ZeroMemory(key);
            }

            if (!returnPlaintextToCaller && plaintext is not null)
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }

            CryptographicOperations.ZeroMemory(nonce);
            CryptographicOperations.ZeroMemory(ciphertext);
            CryptographicOperations.ZeroMemory(tag);
        }
    }

    private static bool TryGetBase64(JsonElement envelope, string propertyName, out byte[] value)
    {
        value = [];

        if (!envelope.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        try
        {
            value = Convert.FromBase64String(property.GetString()!);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
