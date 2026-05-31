namespace Coffer.Infrastructure.Parsing.Pko;

/// <summary>
/// Thrown by <see cref="PkoBpStatementParser"/> when the detected PKO BP layout
/// is not one we support yet. Sprint 7 supports only the standard checking
/// layout ("Wyciąg z rachunku"); Sprint 8 adds credit-card / savings /
/// foreign-currency layouts and the throw goes away for those.
/// </summary>
public sealed class UnsupportedPkoLayoutException : Exception
{
    public UnsupportedPkoLayoutException(string layoutHint)
        : base($"PKO BP statement layout '{layoutHint}' is not supported in this build.")
    {
        LayoutHint = layoutHint;
    }

    public string LayoutHint { get; }
}
