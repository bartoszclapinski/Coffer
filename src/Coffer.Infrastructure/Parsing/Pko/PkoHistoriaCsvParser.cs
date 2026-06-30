using System.Globalization;
using System.Text;
using Coffer.Core.Parsing;
using Coffer.Infrastructure.Parsing.Polish;
using Coffer.Shared.Parsing;
using CsvHelper;
using CsvHelper.Configuration;

namespace Coffer.Infrastructure.Parsing.Pko;

/// <summary>
/// Parses the PKO BP "Historia rachunku" CSV export (the freely-available
/// on-demand operation list) into a <see cref="ParseResult"/>. This is the PKO
/// path — the paid "Wyciąg z rachunku" PDF parser was dropped in Sprint 8.
/// </summary>
/// <remarks>
/// The export is Windows-1250, comma-separated, every field quoted, with a fixed
/// 7-named-column header followed by unnamed overflow columns that hold the
/// description sub-fields. Fields are read positionally (the overflow columns
/// share an empty header name, so name-based mapping is not viable). Amounts are
/// signed dot-decimal; dates are ISO. Account number and statement period are not
/// in the body — the period is derived from the row dates and the account number
/// is left empty with a warning (the Phase-2 import flow asks the user to confirm
/// the target account).
/// </remarks>
public sealed class PkoHistoriaCsvParser : IStatementParser
{
    private static readonly Encoding _windows1250;

    static PkoHistoriaCsvParser()
    {
        // Windows-1250 is not a built-in code page on modern .NET; registering
        // the provider is idempotent and safe to call more than once. Register
        // before resolving the encoding (field initialisers run first otherwise).
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        _windows1250 = Encoding.GetEncoding(1250);
    }

    /// <summary>The seven named header columns, in order, that identify the layout.</summary>
    private static readonly string[] _expectedHeader =
    {
        "Data operacji",
        "Data waluty",
        "Typ transakcji",
        "Kwota",
        "Waluta",
        "Saldo po transakcji",
        "Opis transakcji",
    };

    private const int DescriptionStartColumn = 6;

    /// <summary>
    /// Emitted on every parse because the "Historia rachunku" CSV body carries no
    /// account number. The import flow always resolves it (the user confirms the
    /// target account), so <see cref="Coffer.Core.Import.IImportStatementUseCase"/>
    /// drops this from the user-facing summary — it is a parser-level note, not a
    /// problem with the import.
    /// </summary>
    public const string AccountNumberAbsentWarning =
        "Account number is not present in the PKO 'Historia rachunku' CSV export; it must be confirmed at import time.";

    public string BankCode => "PKO_BP";

    public StatementFormat Format => StatementFormat.Csv;

    public bool CanHandle(BankFingerprint fingerprint)
    {
        ArgumentNullException.ThrowIfNull(fingerprint);
        return string.Equals(fingerprint.BankCode, BankCode, StringComparison.Ordinal);
    }

    public async Task<ParseResult> ParseAsync(StatementInput input, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);

        input.Content.Position = 0;
        using var reader = new StreamReader(
            input.Content,
            _windows1250,
            detectEncodingFromByteOrderMarks: false,
            leaveOpen: true);

        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = false,
            Delimiter = ",",
            DetectDelimiter = false,
            BadDataFound = null,
            MissingFieldFound = null,
        };
        using var csv = new CsvReader(reader, config);

        if (!await csv.ReadAsync().ConfigureAwait(false))
        {
            throw new UnsupportedCsvLayoutException("file is empty");
        }

        ValidateHeader(csv);

        var transactions = new List<ParsedTransaction>();
        var warnings = new List<string>();
        string? currency = null;
        var mixedCurrencyWarned = false;

        while (await csv.ReadAsync().ConfigureAwait(false))
        {
            ct.ThrowIfCancellationRequested();

            if (IsBlankRow(csv))
            {
                continue;
            }

            var transaction = ParseRow(csv);
            transactions.Add(transaction);

            if (currency is null)
            {
                currency = transaction.Currency;
            }
            else if (!mixedCurrencyWarned &&
                     !string.Equals(currency, transaction.Currency, StringComparison.Ordinal))
            {
                warnings.Add(
                    "Statement contains more than one currency; ParseResult.Currency reflects the first row only.");
                mixedCurrencyWarned = true;
            }
        }

        warnings.Add(AccountNumberAbsentWarning);

        var periodFrom = transactions.Count > 0 ? transactions.Min(t => t.Date) : default;
        var periodTo = transactions.Count > 0 ? transactions.Max(t => t.Date) : default;
        if (transactions.Count == 0)
        {
            warnings.Add("No transaction rows found in the CSV.");
        }

        return new ParseResult(
            BankCode,
            AccountNumber: string.Empty,
            Currency: currency ?? string.Empty,
            PeriodFrom: periodFrom,
            PeriodTo: periodTo,
            Transactions: transactions,
            Confidence: ParserConfidence.High,
            Warnings: warnings);
    }

    private static void ValidateHeader(CsvReader csv)
    {
        if (csv.Parser.Count < _expectedHeader.Length)
        {
            throw new UnsupportedCsvLayoutException(
                $"expected at least {_expectedHeader.Length} columns, found {csv.Parser.Count}");
        }

        for (var i = 0; i < _expectedHeader.Length; i++)
        {
            var actual = (csv.GetField(i) ?? string.Empty).Trim();
            if (!string.Equals(actual, _expectedHeader[i], StringComparison.OrdinalIgnoreCase))
            {
                throw new UnsupportedCsvLayoutException($"unexpected header in column {i}");
            }
        }
    }

    private static ParsedTransaction ParseRow(CsvReader csv)
    {
        var operationDateRaw = Field(csv, 0);
        if (!PolishDateParser.TryParse(operationDateRaw, out var date))
        {
            throw new UnsupportedCsvLayoutException("operation date is not a valid date");
        }

        DateOnly? bookingDate = null;
        var bookingRaw = Field(csv, 1);
        if (!string.IsNullOrWhiteSpace(bookingRaw))
        {
            if (!PolishDateParser.TryParse(bookingRaw, out var booking))
            {
                throw new UnsupportedCsvLayoutException("value date is not a valid date");
            }
            bookingDate = booking;
        }

        var amount = ParseAmount(Field(csv, 3));
        var currency = Field(csv, 4);
        var (description, merchant) = BuildDescription(csv);

        return new ParsedTransaction(date, bookingDate, amount, currency, description, merchant);
    }

    private static decimal ParseAmount(string raw)
    {
        var cleaned = raw.Replace(" ", string.Empty, StringComparison.Ordinal).TrimStart('+');
        if (!decimal.TryParse(cleaned, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var amount))
        {
            throw new UnsupportedCsvLayoutException("amount is not a valid signed decimal");
        }
        return amount;
    }

    private static (string Description, string? Merchant) BuildDescription(CsvReader csv)
    {
        var parts = new List<string>();
        string? merchant = null;

        for (var i = DescriptionStartColumn; i < csv.Parser.Count; i++)
        {
            var value = Field(csv, i);
            if (value.Length == 0)
            {
                continue;
            }

            parts.Add(value);
            merchant ??= TryExtractMerchant(value);
        }

        return (string.Join(' ', parts), merchant);
    }

    private static string? TryExtractMerchant(string field)
    {
        // Labelled sub-field e.g. "Nazwa nadawcy/odbiorcy: JAN KOWALSKI".
        const string label = "Nazwa ";
        if (!field.StartsWith(label, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var colon = field.IndexOf(':', StringComparison.Ordinal);
        if (colon < 0 || colon == field.Length - 1)
        {
            return null;
        }

        var value = field[(colon + 1)..].Trim();
        return value.Length == 0 ? null : value;
    }

    /// <summary>Reads a field by index, stripping the Excel text-guard leading <c>'</c> and trimming.</summary>
    private static string Field(CsvReader csv, int index)
    {
        var raw = csv.GetField(index);
        if (string.IsNullOrEmpty(raw))
        {
            return string.Empty;
        }

        var trimmed = raw.Trim();
        if (trimmed.StartsWith('\''))
        {
            trimmed = trimmed[1..].Trim();
        }
        return trimmed;
    }

    private static bool IsBlankRow(CsvReader csv)
    {
        for (var i = 0; i < csv.Parser.Count; i++)
        {
            if (!string.IsNullOrWhiteSpace(csv.GetField(i)))
            {
                return false;
            }
        }
        return true;
    }
}
