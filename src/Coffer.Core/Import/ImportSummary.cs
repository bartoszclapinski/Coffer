namespace Coffer.Core.Import;

/// <summary>
/// Outcome of an import run. <see cref="Added"/> + <see cref="Skipped"/> equals the
/// number of parsed transactions; <see cref="Skipped"/> covers rows whose
/// <c>Hash</c> already existed (dedup). <see cref="AlreadyImported"/> is set when a
/// prior <c>ImportSession</c> recorded the same file hash — a fast "you already
/// imported this exact file" signal, independent of per-row dedup.
/// </summary>
/// <param name="ImportSessionId">The session row created for this run.</param>
/// <param name="Added">Transactions newly persisted.</param>
/// <param name="Skipped">Parsed transactions skipped as duplicates.</param>
/// <param name="Categorized">Newly persisted transactions that got a category (rules/cache).</param>
/// <param name="AlreadyImported">True when an earlier session imported a byte-identical file.</param>
/// <param name="Warnings">Non-fatal issues surfaced by the parser (and the import).</param>
public sealed record ImportSummary(
    Guid ImportSessionId,
    int Added,
    int Skipped,
    int Categorized,
    bool AlreadyImported,
    IReadOnlyList<string> Warnings);
