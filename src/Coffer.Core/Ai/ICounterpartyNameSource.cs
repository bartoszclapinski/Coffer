namespace Coffer.Core.Ai;

/// <summary>
/// Supplies the counterparty names already known from imported statements (the values of the
/// labelled "Nazwa nadawcy / odbiorcy" sub-fields). The anonymiser pseudonymises these wherever they
/// appear in a prompt, which covers names that reach a prompt without their label: a transaction's
/// <c>Merchant</c>, a detected recurring-flow name, an anomaly title.
/// </summary>
public interface ICounterpartyNameSource
{
    Task<IReadOnlyCollection<string>> GetKnownCounterpartyNamesAsync(CancellationToken ct);
}
