namespace Coffer.Core.Security;

public interface IKeyVault
{
    Task<byte[]?> GetCachedMasterKeyAsync(CancellationToken ct);
    Task SetCachedMasterKeyAsync(byte[] masterKey, TimeSpan ttl, CancellationToken ct);
    Task InvalidateMasterKeyCacheAsync(CancellationToken ct);
}
