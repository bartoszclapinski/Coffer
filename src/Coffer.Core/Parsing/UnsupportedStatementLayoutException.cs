namespace Coffer.Core.Parsing;

/// <summary>
/// Base for parser exceptions meaning "this file is from a supported bank, but its
/// content does not have the layout the parser expects" — e.g. a CSV re-saved by a
/// spreadsheet app with changed columns or separators. Lives in <c>Coffer.Core</c> so
/// the import screen can show a specific, localized hint without referencing the
/// Infrastructure parser that threw it.
/// </summary>
/// <remarks>
/// Messages carry structural hints only — never raw statement content — so the
/// exception is safe to log by type (hard rules #6 / #11).
/// </remarks>
public abstract class UnsupportedStatementLayoutException : Exception
{
    protected UnsupportedStatementLayoutException(string message)
        : base(message)
    {
    }
}
