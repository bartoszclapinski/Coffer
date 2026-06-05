using System.Collections.Concurrent;
using Coffer.Core.Security;

namespace Coffer.Infrastructure.Security;

/// <summary>
/// Non-persistent <see cref="ISecretStore"/> for non-Windows hosts and tests. Secrets
/// live only for the process lifetime — there is no DPAPI equivalent to lean on
/// cross-platform, so a restart loses them and the user re-enters the key. Mirrors the
/// <see cref="InMemoryKeyVault"/> fallback.
/// </summary>
public sealed class InMemorySecretStore : ISecretStore
{
    private readonly ConcurrentDictionary<string, string> _secrets = new(StringComparer.Ordinal);

    public Task<string?> GetSecretAsync(string name, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        return Task.FromResult(_secrets.TryGetValue(name, out var value) ? value : null);
    }

    public Task SetSecretAsync(string name, string value, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(value);
        _secrets[name] = value;
        return Task.CompletedTask;
    }

    public Task DeleteSecretAsync(string name, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        _secrets.TryRemove(name, out _);
        return Task.CompletedTask;
    }
}
