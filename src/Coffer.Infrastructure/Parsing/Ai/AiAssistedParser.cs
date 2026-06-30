using System.Globalization;
using System.Text;
using Coffer.Core.Ai;
using Coffer.Core.Parsing;
using Coffer.Core.Security;
using Coffer.Shared.Parsing;
using Microsoft.Extensions.Logging;
using UglyToad.PdfPig;

namespace Coffer.Infrastructure.Parsing.Ai;

/// <summary>
/// Last-resort fallback parser for banks with no deterministic parser (doc 03 §"AI-assisted
/// fallback parser"). It extracts the statement text, anonymises it, and asks the reasoning-tier
/// model for the transactions as structured JSON — the one parser whose output is not
/// deterministic, so it always reports <see cref="ParserConfidence.Medium"/> and a review warning.
/// </summary>
/// <remarks>
/// Wired into <c>StatementParserRegistry</c> as the explicit fallback, not as a keyed parser, so a
/// deterministic parser always wins for a known bank+format. The opt-in / API-key / budget gating
/// lives here (not in the registry): when AI parsing is unavailable it throws
/// <see cref="UnsupportedBankException"/> so the import flow's existing catch path lights up with no
/// call-site change. The prompt is anonymised (hard rule #7), including the owner's name from
/// settings; the account number is never trusted from the model (left empty, like the PKO CSV
/// parser — the import flow confirms the account). One call is metered as
/// <see cref="AiPurpose.ParserFallback"/>.
/// </remarks>
public sealed class AiAssistedParser : IStatementParser
{
    public const string AiFallbackBankCode = "AI_FALLBACK";

    public const string ReviewWarning =
        "This statement was parsed by the AI fallback parser — review the extracted transactions before saving.";

    public const string AccountNumberAbsentWarning =
        "Account number is not extracted by the AI fallback; it must be confirmed at import time.";

    public const string OwnerNameUnsetWarning =
        "No account-holder name is configured, so the statement header may have reached the AI provider un-redacted. "
        + "Set it in Settings to redact it on future imports.";

    private const int CharsPerToken = 4;
    private const int EstimatedOutputTokens = 2048;
    private const int MaxPromptChars = 60_000;

    private const string SystemPrompt =
        "You extract transactions from a Polish bank statement. The user message is the statement text "
        + "(account/IBAN/NIP and the holder's name are already redacted). Return ONLY JSON matching this "
        + "shape: {\"currency\":\"PLN\",\"periodFrom\":\"YYYY-MM-DD\",\"periodTo\":\"YYYY-MM-DD\","
        + "\"transactions\":[{\"date\":\"YYYY-MM-DD\",\"bookingDate\":\"YYYY-MM-DD or null\","
        + "\"amount\":-12.34,\"currency\":\"PLN\",\"description\":\"...\",\"merchant\":\"... or null\"}]}. "
        + "Rules: dates as ISO yyyy-MM-dd; amount is a signed decimal number (debits negative, credits "
        + "positive), never a string, no thousands separators; copy descriptions verbatim; omit nothing and "
        + "invent nothing. If a value is unknown use null.";

    private readonly IAiProvider _provider;
    private readonly IAiBudgetGate _budgetGate;
    private readonly IAiUsageLedger _ledger;
    private readonly IAiPricing _pricing;
    private readonly IPromptAnonymizer _anonymizer;
    private readonly IAiSettings _settings;
    private readonly ISecretStore _secretStore;
    private readonly ILogger<AiAssistedParser> _logger;

    public AiAssistedParser(
        IAiProvider provider,
        IAiBudgetGate budgetGate,
        IAiUsageLedger ledger,
        IAiPricing pricing,
        IPromptAnonymizer anonymizer,
        IAiSettings settings,
        ISecretStore secretStore,
        ILogger<AiAssistedParser> logger)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(budgetGate);
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(pricing);
        ArgumentNullException.ThrowIfNull(anonymizer);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(secretStore);
        ArgumentNullException.ThrowIfNull(logger);
        _provider = provider;
        _budgetGate = budgetGate;
        _ledger = ledger;
        _pricing = pricing;
        _anonymizer = anonymizer;
        _settings = settings;
        _secretStore = secretStore;
        _logger = logger;
    }

    public string BankCode => AiFallbackBankCode;

    // Nominal only: the fallback is resolved explicitly, never via the (BankCode, Format) lookup.
    public StatementFormat Format => StatementFormat.Pdf;

    public bool CanHandle(BankFingerprint fingerprint) => true;

    public async Task<ParseResult> ParseAsync(StatementInput input, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (!await _settings.GetAiFallbackParsingEnabledAsync(ct).ConfigureAwait(false))
        {
            throw new UnsupportedBankException(AiFallbackBankCode);
        }

        if (!await HasApiKeyAsync(ct).ConfigureAwait(false))
        {
            throw new UnsupportedBankException(AiFallbackBankCode);
        }

        var warnings = new List<string> { ReviewWarning, AccountNumberAbsentWarning };

        var (text, truncated) = ExtractText(input);
        if (truncated)
        {
            warnings.Add($"Statement text exceeded {MaxPromptChars} characters and was truncated before sending to the AI.");
        }

        var ownerNames = ParseOwnerNames(await _settings.GetOwnerIdentityNamesAsync(ct).ConfigureAwait(false));
        if (ownerNames.Count == 0)
        {
            warnings.Add(OwnerNameUnsetWarning);
        }

        var prompt = _anonymizer.Anonymize(text, ownerNames);
        var model = AiDefaults.ChatModel;

        var estInputTokens = (SystemPrompt.Length + prompt.Length) / CharsPerToken;
        var estimate = _pricing.Estimate(model, estInputTokens, EstimatedOutputTokens);
        if (!await _budgetGate.CanProceedAsync(estimate.Pln, AiPriority.Normal, ct).ConfigureAwait(false))
        {
            _logger.LogInformation("Budget gate blocked the AI fallback parse; surfacing as unsupported bank.");
            throw new UnsupportedBankException(AiFallbackBankCode);
        }

        var request = new AiRequest
        {
            Prompt = prompt,
            Model = model,
            SystemPrompt = SystemPrompt,
            MaxTokens = 4096,
            Temperature = 0.1,
        };

        var response = await _provider.CompleteJsonAsync<AiStatementDto>(request, ct).ConfigureAwait(false);
        await _ledger.RecordAsync(response.Usage, AiPurpose.ParserFallback, ct).ConfigureAwait(false);

        return MapResult(response.Value, warnings);
    }

    private async Task<bool> HasApiKeyAsync(CancellationToken ct)
    {
        var provider = await _settings.GetActiveProviderAsync(ct).ConfigureAwait(false);
        var secretKey = string.Equals(provider, AiDefaults.OpenAiProvider, StringComparison.OrdinalIgnoreCase)
            ? AiDefaults.OpenAiApiKeySecret
            : AiDefaults.ClaudeApiKeySecret;
        var key = await _secretStore.GetSecretAsync(secretKey, ct).ConfigureAwait(false);
        return !string.IsNullOrWhiteSpace(key);
    }

    private static (string Text, bool Truncated) ExtractText(StatementInput input)
    {
        var raw = input.Format == StatementFormat.Pdf ? ReadPdfText(input) : ReadCsvText(input);
        return raw.Length > MaxPromptChars ? (raw[..MaxPromptChars], true) : (raw, false);
    }

    private static string ReadPdfText(StatementInput input)
    {
        input.Content.Position = 0;
        using var buffer = new MemoryStream();
        input.Content.CopyTo(buffer);
        input.Content.Position = 0;

        using var document = PdfDocument.Open(buffer.ToArray());
        var sb = new StringBuilder();
        for (var page = 1; page <= document.NumberOfPages; page++)
        {
            sb.AppendLine(document.GetPage(page).Text);
        }

        return sb.ToString();
    }

    private static string ReadCsvText(StatementInput input)
    {
        input.Content.Position = 0;
        using var reader = new StreamReader(input.Content, leaveOpen: true);
        var text = reader.ReadToEnd();
        input.Content.Position = 0;
        return text;
    }

    private ParseResult MapResult(AiStatementDto? dto, List<string> warnings)
    {
        var fallbackCurrency = NormalizeCurrency(dto?.Currency) ?? "PLN";

        var transactions = new List<ParsedTransaction>();
        var skipped = 0;
        foreach (var row in dto?.Transactions ?? [])
        {
            if (!TryParseDate(row.Date, out var date) || row.Amount is not { } amount)
            {
                skipped++;
                continue;
            }

            var bookingDate = TryParseDate(row.BookingDate, out var booking) ? booking : (DateOnly?)null;
            var currency = NormalizeCurrency(row.Currency) ?? fallbackCurrency;
            var description = row.Description?.Trim() ?? string.Empty;
            var merchant = string.IsNullOrWhiteSpace(row.Merchant) ? null : row.Merchant.Trim();

            transactions.Add(new ParsedTransaction(date, bookingDate, amount, currency, description, merchant));
        }

        if (skipped > 0)
        {
            warnings.Add($"{skipped} transaction row(s) returned by the AI could not be parsed and were skipped.");
        }

        if (transactions.Count == 0)
        {
            warnings.Add("The AI fallback returned no usable transactions.");
        }

        var periodFrom = TryParseDate(dto?.PeriodFrom, out var from)
            ? from
            : transactions.Count > 0 ? transactions.Min(t => t.Date) : default;
        var periodTo = TryParseDate(dto?.PeriodTo, out var to)
            ? to
            : transactions.Count > 0 ? transactions.Max(t => t.Date) : default;

        return new ParseResult(
            AiFallbackBankCode,
            AccountNumber: string.Empty,
            Currency: fallbackCurrency,
            PeriodFrom: periodFrom,
            PeriodTo: periodTo,
            Transactions: transactions,
            Confidence: ParserConfidence.Medium,
            Warnings: warnings);
    }

    private static bool TryParseDate(string? raw, out DateOnly date)
    {
        date = default;
        return !string.IsNullOrWhiteSpace(raw)
            && DateOnly.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }

    private static string? NormalizeCurrency(string? raw)
    {
        var trimmed = raw?.Trim().ToUpperInvariant();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static IReadOnlyList<string> ParseOwnerNames(string? raw) =>
        string.IsNullOrWhiteSpace(raw)
            ? []
            : raw.Split([',', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

    private sealed record AiStatementDto(
        string? Currency,
        string? PeriodFrom,
        string? PeriodTo,
        IReadOnlyList<AiTransactionDto>? Transactions);

    private sealed record AiTransactionDto(
        string? Date,
        string? BookingDate,
        decimal? Amount,
        string? Currency,
        string? Description,
        string? Merchant);
}
