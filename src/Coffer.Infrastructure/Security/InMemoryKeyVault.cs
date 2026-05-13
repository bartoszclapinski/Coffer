using Coffer.Core.Security;

namespace Coffer.Infrastructure.Security;

/// <summary>
/// Cross-platform fallback used on non-Windows hosts (CI, Linux/macOS dev environments).
/// Not suitable for production: state is lost on process exit and there is no on-disk persistence.
/// Production on Windows uses <see cref="WindowsDpapiKeyVault"/>; mobile will use MAUI SecureStorage in a later sprint.
/// </summary>
public sealed class InMemoryKeyVault : IKeyVault
{
    private readonly object _lock = new();
    private byte[]? _cachedKey;
    private DateTime _expiresAtUtc = DateTime.MinValue;

    public Task<byte[]?> GetCachedMasterKeyAsync(CancellationToken ct)
    {
        lock (_lock)
        {
            if (_cachedKey is null || DateTime.UtcNow > _expiresAtUtc)
            {
                ClearCachedKey();
                return Task.FromResult<byte[]?>(null);
            }

            return Task.FromResult<byte[]?>((byte[])_cachedKey.Clone());
        }
    }

    public Task SetCachedMasterKeyAsync(byte[] masterKey, TimeSpan ttl, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(masterKey);

        lock (_lock)
        {
            ClearCachedKey();
            _cachedKey = (byte[])masterKey.Clone();
            _expiresAtUtc = DateTime.UtcNow.Add(ttl);
        }

        return Task.CompletedTask;
    }

    public Task InvalidateMasterKeyCacheAsync(CancellationToken ct)
    {
        lock (_lock)
        {
            ClearCachedKey();
            _expiresAtUtc = DateTime.MinValue;
        }

        return Task.CompletedTask;
    }

    private void ClearCachedKey()
    {
        if (_cachedKey is not null)
        {
            Array.Clear(_cachedKey, 0, _cachedKey.Length);
            _cachedKey = null;
        }
    }
}
