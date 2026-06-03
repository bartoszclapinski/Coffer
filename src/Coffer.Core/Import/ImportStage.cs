namespace Coffer.Core.Import;

/// <summary>
/// The five sequential stages of a statement import, reported through
/// <see cref="System.IProgress{T}"/> of <see cref="ImportProgress"/> so the UI can
/// show a step indicator. Emitted in declaration order.
/// </summary>
public enum ImportStage
{
    ReadingFile,
    DetectingBank,
    Parsing,
    Deduplicating,
    Saving,
}
