using System.Text.RegularExpressions;

namespace Coffer.Infrastructure.AI;

/// <summary>
/// Tells a counterparty name that is safe to send to the AI (a legal entity: a shop, a company, a
/// public institution) from one that names or may name a person (Sprint 30). Legal-entity names are
/// the categorisation signal (<c>ZUS</c>, <c>KAWIARNIA CENTRUM</c>, <c>ORANGE POLSKA S.A.</c>) and are
/// public register data, so they are kept; everything else is pseudonymised.
/// <para>
/// When unsure, the answer is "person": privacy wins over category quality. In particular a sole
/// trader's business name legally contains the owner's own name (<c>P.H.U. "ANEX" ANNA NOWAK</c>), and
/// civil / general / limited partnerships are usually named after their partners, so those markers
/// count as a person, as does any recognised Polish first name. Word count is deliberately not a
/// signal: a four-word sole-trader name is still a person's name.
/// </para>
/// </summary>
public static partial class CounterpartyNames
{
    // Words that name an organisation, not a person (folded, upper case, whole words).
    private static readonly HashSet<string> _organisationWords = new(StringComparer.Ordinal)
    {
        // Public bodies and non-profits.
        "URZAD", "ZUS", "KRUS", "ZAKLAD", "GMINA", "MIASTO", "POWIAT", "STAROSTWO", "WOJEWODZTWO",
        "SKARB", "MINISTERSTWO", "BANK", "TOWARZYSTWO", "FUNDACJA", "STOWARZYSZENIE", "SPOLDZIELNIA",
        "WSPOLNOTA", "PARAFIA", "SZKOLA", "PRZEDSZKOLE", "ZLOBEK", "UNIWERSYTET", "POLITECHNIKA",
        "AKADEMIA", "SZPITAL", "PRZYCHODNIA", "KLINIKA", "POCZTA",

        // Shops and services.
        "SKLEP", "MARKET", "SUPERMARKET", "HIPERMARKET", "APTEKA", "DROGERIA", "KAWIARNIA",
        "RESTAURACJA", "PIZZERIA", "BISTRO", "BAR", "PIEKARNIA", "CUKIERNIA", "KSIEGARNIA", "KWIACIARNIA",
        "OPTYK", "STACJA", "HOTEL", "HOSTEL", "SALON", "KINO", "TEATR", "MUZEUM", "SERWIS", "WARSZTAT",
        "PARKING", "CENTRUM", "GALERIA", "OUTLET", "STORE", "SHOP", "ONLINE",

        // Corporate wording.
        "POLSKA", "POLAND", "EUROPE", "INTERNATIONAL", "GROUP", "SERVICES", "PAYMENTS",
    };

    // Markers of a business that carries a person's name: sole traders and partnerships of persons.
    private static readonly HashSet<string> _personBusinessWords = new(StringComparer.Ordinal)
    {
        "PHU", "FHU", "PPHU", "ZUH", "PUH", "FH", "PH", "PW", "FIRMA", "USLUGI", "HANDEL", "WSPOLNICY",
    };

    // Common Polish first names (folded, upper case). Any of them in a name means a person.
    private static readonly HashSet<string> _firstNames = new(StringComparer.Ordinal)
    {
        "ADAM", "ADRIAN", "ALBERT", "ALEKSANDER", "ANDRZEJ", "ANTONI", "ARKADIUSZ", "ARTUR", "BARTOSZ",
        "BARTLOMIEJ", "BOGDAN", "BOGUSLAW", "BOLESLAW", "BRUNO", "CZESLAW", "DAMIAN", "DANIEL", "DARIUSZ",
        "DAWID", "DOMINIK", "EDMUND", "EDWARD", "EMIL", "ERYK", "EUGENIUSZ", "FILIP", "FRANCISZEK",
        "GABRIEL", "GRZEGORZ", "HENRYK", "HUBERT", "IGNACY", "IGOR", "IRENEUSZ", "JACEK", "JAKUB", "JAN",
        "JANUSZ", "JAROSLAW", "JERZY", "JOZEF", "JULIAN", "KACPER", "KAMIL", "KAROL", "KAZIMIERZ",
        "KONRAD", "KRYSTIAN", "KRZYSZTOF", "KSAWERY", "LECH", "LEON", "LESZEK", "LUCJAN", "LUKASZ",
        "MACIEJ", "MAKSYMILIAN", "MARCEL", "MARCIN", "MAREK", "MARIAN", "MARIUSZ", "MATEUSZ",
        "MICHAL", "MIECZYSLAW", "MIKOLAJ", "MIROSLAW", "NIKODEM", "NORBERT", "OLIWER", "OSKAR", "PATRYK",
        "PAWEL", "PIOTR", "PRZEMYSLAW", "RADOSLAW", "RAFAL", "ROBERT", "ROMAN", "RYSZARD", "SEBASTIAN",
        "SLAWOMIR", "STANISLAW", "STEFAN", "SZYMON", "TADEUSZ", "TOMASZ", "TYMON", "TYMOTEUSZ", "WACLAW",
        "WALDEMAR", "WIESLAW", "WIKTOR", "WITOLD", "WLADYSLAW", "WOJCIECH", "ZBIGNIEW", "ZDZISLAW",
        "ZENON",
        "ADRIANNA", "AGATA", "AGNIESZKA", "ALEKSANDRA", "ALICJA", "AMELIA", "ANETA", "ANGELIKA", "ANIELA",
        "ANITA", "ANNA", "ANTONINA", "BARBARA", "BEATA", "BLANKA", "BOGUMILA", "BOZENA", "CZESLAWA",
        "DANUTA", "DOMINIKA", "DOROTA", "EDYTA", "ELIZA", "ELZBIETA", "EMILIA", "EWA", "GABRIELA",
        "GENOWEFA", "GRAZYNA", "HALINA", "HANNA", "HELENA", "IGA", "ILONA", "IRENA", "IWONA", "IZABELA",
        "JADWIGA", "JAGODA", "JANINA", "JOANNA", "JOLANTA", "JULIA", "JUSTYNA", "KAMILA", "KAROLINA",
        "KATARZYNA", "KINGA", "KLAUDIA", "KORNELIA", "KRYSTYNA", "LAURA", "LENA", "LEOKADIA", "LIDIA",
        "LUCYNA", "MAGDALENA", "MAJA", "MALGORZATA", "MARIA", "MARIANNA", "MARIOLA", "MARTA", "MARZENA",
        "MICHALINA", "MILENA", "MONIKA", "NATALIA", "NIKOLA", "NINA", "OLGA", "OLIWIA", "PATRYCJA",
        "PAULINA", "REGINA", "RENATA", "ROZA", "STANISLAWA", "SYLWIA", "TERESA", "URSZULA", "WANDA",
        "WERONIKA", "WIESLAWA", "WIKTORIA", "WIOLETTA", "ZDZISLAWA", "ZOFIA", "ZOJA", "ZUZANNA",
    };

    /// <summary>
    /// Whether <paramref name="name"/> is a legal entity whose name may be sent to the AI unchanged.
    /// <c>false</c> for anything that is, or may contain, a person's name.
    /// </summary>
    public static bool IsLegalEntity(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var key = PolishText.Key(name);
        if (key.Length > 1 && key[0] == '[')
        {
            return false; // a placeholder ([PARTY_1], [OWNER]) is not a name to keep
        }

        // Capital companies and foreign corporate forms are registered entities even when named
        // after a founder ("KOWALSKI SP. Z O.O."): their names are public register data.
        if (CorporateFormRegex().IsMatch(key))
        {
            return true;
        }

        var words = WordRegex().Matches(key).Select(m => m.Value).ToList();
        if (words.Count == 0)
        {
            return false;
        }

        if (words.Any(_firstNames.Contains)
            || words.Any(_personBusinessWords.Contains)
            || PartnershipRegex().IsMatch(key))
        {
            return false;
        }

        if (words.Any(_organisationWords.Contains) || key.Any(char.IsDigit) || DomainRegex().IsMatch(key))
        {
            return true;
        }

        // One word that is not a first name is a brand ("ALLEGRO", "NETFLIX"): a transfer to a
        // person carries at least a first name and a surname.
        return words.Count == 1;
    }

    // Letter runs. Dotted abbreviations ("P.H.U.") split into single letters here; PartnershipRegex
    // recognises them in the full key instead.
    [GeneratedRegex(@"\p{L}+")]
    private static partial Regex WordRegex();

    // SP. Z O.O. / SPOLKA Z O.O. (with any spacing/dots), SPOLKA Z OGRANICZONA ..., S.A., SPOLKA
    // AKCYJNA, S.K.A., and foreign forms: GMBH, AG, SE, B.V., N.V., LTD, LIMITED, INC, LLC, PLC,
    // S.A.R.L., S.R.L., AB.
    [GeneratedRegex(
        @"\bSP(?:OLKA)?\.?\s*Z\s*O\.?\s*O\b|\bSPOLKA\s+Z\s+OGRANICZONA\b|\bSPOLKA\s+AKCYJNA\b|(?<![\p{L}.])S\.?\s?A\.?(?![\p{L}])$"
        + @"|(?<![\p{L}])S\.\s?A\.(?![\p{L}])|\bS\.?\s?K\.?\s?A\.?(?![\p{L}])"
        + @"|\b(?:GMBH|AG|SE|LTD|LIMITED|INC|LLC|PLC|AB)\b\.?|(?<![\p{L}])[BN]\.\s?V\.?(?![\p{L}])|\bS\.?\s?A?\.?\s?R\.?\s?L\b",
        RegexOptions.CultureInvariant)]
    private static partial Regex CorporateFormRegex();

    // Sole-trader abbreviations written with dots or spaces (P.H.U., F.H.U., P.P.H.U.) and the
    // partnership forms named after persons: S.C., SP. J., SP. K., SP. P., SPOLKA JAWNA/CYWILNA/...
    [GeneratedRegex(
        @"(?<![\p{L}])(?:P\.?\s?P\.?\s?H\.?\s?U|P\.?\s?H\.?\s?U|F\.?\s?H\.?\s?U|Z\.?\s?U\.?\s?H|P\.?\s?U\.?\s?H)\.?(?![\p{L}])"
        + @"|(?<![\p{L}])S\.?\s?C\.?$|(?<![\p{L}])S\.\s?C\.(?![\p{L}])|\bSP\.?\s?[JKP]\.?(?![\p{L}])"
        + @"|\bSPOLKA\s+(?:JAWNA|CYWILNA|KOMANDYTOWA|PARTNERSKA)\b",
        RegexOptions.CultureInvariant)]
    private static partial Regex PartnershipRegex();

    [GeneratedRegex(@"(?:^|[\s.])WWW\.|\.(?:PL|COM|EU|NET|ORG|IO)\b", RegexOptions.CultureInvariant)]
    private static partial Regex DomainRegex();
}
