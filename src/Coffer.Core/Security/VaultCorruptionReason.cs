namespace Coffer.Core.Security;

/// <summary>
/// Failure modes that <see cref="VaultCorruptedException"/> distinguishes so the UI
/// can show a more useful Polish message than a single generic one.
/// </summary>
public enum VaultCorruptionReason
{
    /// <summary>The DEK file could not be parsed (truncated, wrong version, etc).</summary>
    DekFileFormat,

    /// <summary>An I/O error prevented reading the DEK file.</summary>
    DekFileIo,

    /// <summary>Any other corruption signal not covered by the specific reasons above.</summary>
    Other,
}
