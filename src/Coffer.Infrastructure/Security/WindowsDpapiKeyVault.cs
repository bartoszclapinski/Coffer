using System.Runtime.Versioning;
using System.Security.Cryptography;
using Coffer.Core.Security;

namespace Coffer.Infrastructure.Security;

[SupportedOSPlatform("windows")]
public sealed class WindowsDpapiKeyVault : IKeyVault
{
    private readonly string _cacheFilePath;

    public WindowsDpapiKeyVault() : this(CofferPaths.MasterKeyCacheFile())
    {
    }

    public WindowsDpapiKeyVault(string cacheFilePath)
    {
        _cacheFilePath = cacheFilePath;
    }

    public async Task<byte[]?> GetCachedMasterKeyAsync(CancellationToken ct)
    {
        if (!File.Exists(_cacheFilePath))
        {
            return null;
        }

        var protectedBytes = await File.ReadAllBytesAsync(_cacheFilePath, ct);

        byte[] plainBytes;
        try
        {
            plainBytes = ProtectedData.Unprotect(
                protectedBytes,
                optionalEntropy: null,
                DataProtectionScope.CurrentUser);
        }
        catch (CryptographicException)
        {
            return null;
        }

        try
        {
            if (!TryParsePayload(plainBytes, out var expiresAtUtcTicks, out var masterKey))
            {
                return null;
            }

            if (DateTime.UtcNow.Ticks > expiresAtUtcTicks)
            {
                Array.Clear(masterKey, 0, masterKey.Length);
                TryDeleteCacheFile();
                return null;
            }

            return masterKey;
        }
        finally
        {
            Array.Clear(plainBytes, 0, plainBytes.Length);
        }
    }

    public async Task SetCachedMasterKeyAsync(byte[] masterKey, TimeSpan ttl, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(masterKey);

        var directory = Path.GetDirectoryName(_cacheFilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var expiresAtUtcTicks = DateTime.UtcNow.Add(ttl).Ticks;
        var plainBytes = BuildPayload(expiresAtUtcTicks, masterKey);

        try
        {
            var protectedBytes = ProtectedData.Protect(
                plainBytes,
                optionalEntropy: null,
                DataProtectionScope.CurrentUser);
            await File.WriteAllBytesAsync(_cacheFilePath, protectedBytes, ct);
        }
        finally
        {
            Array.Clear(plainBytes, 0, plainBytes.Length);
        }
    }

    public Task InvalidateMasterKeyCacheAsync(CancellationToken ct)
    {
        TryDeleteCacheFile();
        return Task.CompletedTask;
    }

    private static byte[] BuildPayload(long expiresAtUtcTicks, byte[] masterKey)
    {
        var payload = new byte[sizeof(long) + sizeof(int) + masterKey.Length];
        BitConverter.GetBytes(expiresAtUtcTicks).CopyTo(payload, 0);
        BitConverter.GetBytes(masterKey.Length).CopyTo(payload, sizeof(long));
        Buffer.BlockCopy(masterKey, 0, payload, sizeof(long) + sizeof(int), masterKey.Length);
        return payload;
    }

    private static bool TryParsePayload(byte[] payload, out long expiresAtUtcTicks, out byte[] masterKey)
    {
        const int headerSize = sizeof(long) + sizeof(int);
        if (payload.Length < headerSize)
        {
            expiresAtUtcTicks = 0;
            masterKey = Array.Empty<byte>();
            return false;
        }

        expiresAtUtcTicks = BitConverter.ToInt64(payload, 0);
        var keyLength = BitConverter.ToInt32(payload, sizeof(long));

        if (keyLength < 0 || headerSize + keyLength > payload.Length)
        {
            masterKey = Array.Empty<byte>();
            return false;
        }

        masterKey = new byte[keyLength];
        Buffer.BlockCopy(payload, headerSize, masterKey, 0, keyLength);
        return true;
    }

    private void TryDeleteCacheFile()
    {
        if (!File.Exists(_cacheFilePath))
        {
            return;
        }

        try
        {
            File.Delete(_cacheFilePath);
        }
        catch (IOException)
        {
            // File is held by another process; best-effort delete only.
        }
        catch (UnauthorizedAccessException)
        {
            // Read-only or access denied; best-effort delete only.
        }
    }
}
