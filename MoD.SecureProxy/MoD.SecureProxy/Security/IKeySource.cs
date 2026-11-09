namespace MoD.SecureProxy.Security;

public interface IKeySource
{
    Task<byte[]> GetKeyAsync(string keyReference, CancellationToken cancellationToken);
}
