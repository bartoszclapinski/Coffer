namespace Coffer.Core.Parsing;

/// <summary>
/// Thrown by <see cref="StatementParserRegistry"/> (in <c>Coffer.Infrastructure</c>)
/// when the detected bank does not have a registered <see cref="IStatementParser"/>.
/// Sprint 8 swaps the throw for an AI-assisted parser lookup; until then, importing
/// a statement from an unsupported bank surfaces this exception to the caller.
/// </summary>
public sealed class UnsupportedBankException : Exception
{
    public UnsupportedBankException(string bankCode)
        : base($"No parser is registered for bank code '{bankCode}'.")
    {
        BankCode = bankCode;
    }

    public string BankCode { get; }
}
