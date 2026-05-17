using Coffer.Core.Security;

namespace Coffer.Infrastructure.Security;

/// <summary>
/// Thread-safe in-memory <see cref="IDekHolder"/>. Stores a defensive copy of the DEK
/// and zeros previous bytes on every <see cref="Set"/> / <see cref="Clear"/> call.
/// </summary>
public sealed class DekHolder : IDekHolder
{
    private readonly object _lock = new();
    private byte[]? _dek;

    public bool IsAvailable
    {
        get
        {
            lock (_lock)
            {
                return _dek is not null;
            }
        }
    }

    public byte[] Get()
    {
        lock (_lock)
        {
            if (_dek is null)
            {
                throw new InvalidOperationException(
                    "DEK is not available. Call Set after setup/login before any database operation.");
            }

            return (byte[])_dek.Clone();
        }
    }

    public void Set(byte[] dek)
    {
        ArgumentNullException.ThrowIfNull(dek);

        lock (_lock)
        {
            ClearInternal();
            _dek = (byte[])dek.Clone();
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            ClearInternal();
        }
    }

    private void ClearInternal()
    {
        if (_dek is not null)
        {
            Array.Clear(_dek, 0, _dek.Length);
            _dek = null;
        }
    }
}
