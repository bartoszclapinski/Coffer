using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using Coffer.Core.Security;

namespace Coffer.Infrastructure.Security;

/// <summary>
/// DPAPI-backed <see cref="ISecretStore"/>: each named secret is a UTF-8 string
/// encrypted with <see cref="DataProtectionScope.CurrentUser"/> and written to its own
/// file under <see cref="IVaultPaths.SecretsFolder"/>. The filename is the SHA-256 of
/// the secret name (so arbitrary names like <c>ai.claude.apiKey</c> map to a safe,
/// stable path); the name itself is never written to disk. Same scope and posture as
/// <see cref="WindowsDpapiKeyVault"/>.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsDpapiSecretStore : ISecretStore
{
    private readonly string _secretsFolder;

    public WindowsDpapiSecretStore(IVaultPaths vaultPaths)
    {
        ArgumentNullException.ThrowIfNull(vaultPaths);
        _secretsFolder = vaultPaths.SecretsFolder;
    }

    public async Task<string?> GetSecretAsync(string name, CancellationToken ct)
    {
        var path = ResolvePath(name);
        if (!File.Exists(path))
        {
            return null;
        }

        var protectedBytes = await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);

        byte[] plainBytes;
        try
        {
            plainBytes = ProtectedData.Unprotect(
                protectedBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
        }
        catch (CryptographicException)
        {
            // Written under a different user/machine, or corrupt — treat as absent.
            return null;
        }

        try
        {
            return Encoding.UTF8.GetString(plainBytes);
        }
        finally
        {
            Array.Clear(plainBytes, 0, plainBytes.Length);
        }
    }

    public async Task SetSecretAsync(string name, string value, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(value);

        Directory.CreateDirectory(_secretsFolder);

        var plainBytes = Encoding.UTF8.GetBytes(value);
        try
        {
            var protectedBytes = ProtectedData.Protect(
                plainBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
            await File.WriteAllBytesAsync(ResolvePath(name), protectedBytes, ct).ConfigureAwait(false);
        }
        finally
        {
            Array.Clear(plainBytes, 0, plainBytes.Length);
        }
    }

    public Task DeleteSecretAsync(string name, CancellationToken ct)
    {
        var path = ResolvePath(name);
        if (File.Exists(path))
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
                // Held by another process; best-effort delete only.
            }
            catch (UnauthorizedAccessException)
            {
                // Access denied; best-effort delete only.
            }
        }

        return Task.CompletedTask;
    }

    private string ResolvePath(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(name));
        return Path.Combine(_secretsFolder, Convert.ToHexString(hash) + ".dpapi");
    }
}
