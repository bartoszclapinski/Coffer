namespace Coffer.Shared.Parsing;

/// <summary>
/// Format-neutral input to <c>IBankDetector</c> and <c>IStatementParser</c>. Carries
/// the raw export bytes plus the format so a single detector/registry can route both
/// PDF and CSV statements without leaking PdfPig types into <c>Coffer.Core</c>.
/// </summary>
/// <remarks>
/// The caller owns <see cref="Content"/> and is responsible for disposing it; parsers
/// and detectors must not dispose the stream. The stream must be seekable — a detector
/// may probe it (header/first page) before the parser reads it in full, so consumers
/// reset <see cref="System.IO.Stream.Position"/> to 0 before reading.
/// </remarks>
/// <param name="Content">The raw statement bytes. Seekable; not owned by the reader.</param>
/// <param name="Format">Which export format <paramref name="Content"/> holds.</param>
/// <param name="FileName">Original file name when known (used as a weak detection hint); null otherwise.</param>
public sealed record StatementInput(
    Stream Content,
    StatementFormat Format,
    string? FileName = null);
