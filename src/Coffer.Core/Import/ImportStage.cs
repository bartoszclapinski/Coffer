namespace Coffer.Core.Import;

/// <summary>
/// The sequential stages of a statement import, reported through
/// <see cref="System.IProgress{T}"/> of <see cref="ImportProgress"/> so the UI can
/// show a step indicator. Emitted in declaration order. <see cref="Categorizing"/> is
/// only reported when there are new rows to categorise (it may run an AI batch).
/// </summary>
public enum ImportStage
{
    ReadingFile,
    DetectingBank,
    Parsing,
    Deduplicating,
    Categorizing,
    Saving,
}
