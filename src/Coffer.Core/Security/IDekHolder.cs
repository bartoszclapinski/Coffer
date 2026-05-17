namespace Coffer.Core.Security;

/// <summary>
/// Single-writer in-memory holder for the Database Encryption Key. Bridges the
/// gap between the Sprint 4 <c>AddCofferDatabase</c> lazy <c>dekProvider</c> and
/// the runtime reality that the DEK is only known after setup (Sprint 5) or
/// login (Sprint 6).
/// </summary>
/// <remarks>
/// Implementations must zero the previously-held bytes on every <see cref="Set"/>
/// and <see cref="Clear"/> call. Calling <see cref="Get"/> before any <see cref="Set"/>
/// throws — callers that need to probe should consult <see cref="IsAvailable"/> first.
/// </remarks>
public interface IDekHolder
{
    bool IsAvailable { get; }

    byte[] Get();

    void Set(byte[] dek);

    void Clear();
}
