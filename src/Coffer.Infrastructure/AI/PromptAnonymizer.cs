using Coffer.Core.Ai;

namespace Coffer.Infrastructure.AI;

/// <summary>
/// Opens <see cref="PromptRedactor"/> sessions with everything they need already loaded: the owner's
/// identity names from <see cref="IAiSettings"/> and the counterparty names known from imported
/// statements. Resolving both here, rather than having each AI consumer pass them, is what makes
/// it impossible for a consumer to forget them (hard rule #7): the session is the only redaction API.
/// </summary>
public sealed class PromptAnonymizer : IPromptAnonymizer
{
    private static readonly char[] _ownerNameSeparators = [',', ';', '\n', '\r'];

    private readonly IAiSettings _settings;
    private readonly ICounterpartyNameSource _counterparties;

    public PromptAnonymizer(IAiSettings settings, ICounterpartyNameSource counterparties)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(counterparties);
        _settings = settings;
        _counterparties = counterparties;
    }

    public async Task<IPromptRedactionSession> BeginSessionAsync(CancellationToken ct)
    {
        var ownerNames = ParseOwnerNames(await _settings.GetOwnerIdentityNamesAsync(ct).ConfigureAwait(false));
        var counterpartyNames = await _counterparties.GetKnownCounterpartyNamesAsync(ct).ConfigureAwait(false);
        return new PromptRedactor(ownerNames, counterpartyNames);
    }

    /// <summary>
    /// Splits the owner's raw identity-names setting (several spellings or aliases separated by
    /// commas, semicolons or new lines) into distinct names.
    /// </summary>
    public static IReadOnlyList<string> ParseOwnerNames(string? raw) =>
        string.IsNullOrWhiteSpace(raw)
            ? []
            : raw.Split(_ownerNameSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
}
