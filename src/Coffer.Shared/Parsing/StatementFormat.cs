namespace Coffer.Shared.Parsing;

/// <summary>
/// The file format a bank statement export arrives in. Drives parser selection in
/// <c>StatementParserRegistry</c> and detection in <c>IBankDetector</c>. Only the
/// formats with a working parser are listed; add more (Xml, Xls, Html) when a
/// parser for them exists.
/// </summary>
public enum StatementFormat
{
    /// <summary>PDF export — opened with PdfPig by PDF-based parsers.</summary>
    Pdf,

    /// <summary>CSV export — e.g. PKO BP "Historia rachunku" (Windows-1250).</summary>
    Csv,
}
