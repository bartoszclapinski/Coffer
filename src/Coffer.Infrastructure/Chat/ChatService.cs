using Coffer.Core.Ai;
using Coffer.Core.Chat;
using Coffer.Infrastructure.AI;
using Microsoft.Extensions.Logging;

namespace Coffer.Infrastructure.Chat;

/// <summary>
/// Chat-with-data orchestrator (Phase 7, doc 04). Runs the tool-call loop over an
/// <see cref="IAiProvider"/>: sends the conversation plus the read-only tool menu, executes any
/// tools the model requests, anonymises their output (hard rule #7), feeds the results back, and
/// repeats until the model returns a final answer or the iteration cap is hit. Every model turn is
/// metered into the cost ledger (<c>Purpose = "chat"</c>) and the monthly budget gate is consulted
/// before each turn — chat is non-critical, so it is blocked (no API call) once the cap is reached.
/// Master password / BIP39 never enter any prompt (hard rule #6); the model can never mutate state
/// (tools are read-only).
/// </summary>
public sealed class ChatService : IChatService
{
    private const int MaxIterations = 5;
    private const int CharsPerToken = 4;
    private const int EstimatedOutputTokens = 600;
    private const int ProviderAttempts = 2;
    private static readonly TimeSpan _retryBackoff = TimeSpan.FromMilliseconds(250);

    private const string BudgetMessage =
        "Budżet AI na ten miesiąc został wyczerpany, więc nie zadałem pytania modelowi. "
        + "Zwiększ limit w ustawieniach, aby kontynuować.";

    private const string MissingKeyMessage =
        "Brak skonfigurowanego klucza API. Dodaj klucz w ustawieniach, aby korzystać z asystenta.";

    private const string ErrorMessage =
        "Przepraszam, wystąpił błąd podczas przetwarzania zapytania. Spróbuj ponownie za chwilę.";

    private const string IncompleteMessage =
        "Nie udało mi się dokończyć odpowiedzi. Spróbuj zadać pytanie inaczej.";

    private readonly IAiProvider _provider;
    private readonly IReadOnlyDictionary<string, IChatTool> _tools;
    private readonly IReadOnlyList<AiTool> _toolDescriptors;
    private readonly IAiBudgetGate _budgetGate;
    private readonly IAiUsageLedger _ledger;
    private readonly IAiPricing _pricing;
    private readonly IPromptAnonymizer _anonymizer;
    private readonly ILogger<ChatService> _logger;

    public ChatService(
        IAiProvider provider,
        IEnumerable<IChatTool> tools,
        IAiBudgetGate budgetGate,
        IAiUsageLedger ledger,
        IAiPricing pricing,
        IPromptAnonymizer anonymizer,
        ILogger<ChatService> logger)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(budgetGate);
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(pricing);
        ArgumentNullException.ThrowIfNull(anonymizer);
        ArgumentNullException.ThrowIfNull(logger);

        _provider = provider;
        _tools = tools.ToDictionary(t => t.Name, StringComparer.Ordinal);
        _toolDescriptors = _tools.Values.Select(t => t.ToAiTool()).ToList();
        _budgetGate = budgetGate;
        _ledger = ledger;
        _pricing = pricing;
        _anonymizer = anonymizer;
        _logger = logger;
    }

    public async Task<ChatTurn> AskAsync(
        string question, IReadOnlyList<ChatMessage> history, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);
        ArgumentNullException.ThrowIfNull(history);

        // One redaction session per question: the history, the question and every tool result share
        // it, so a counterparty keeps the same pseudonym across the whole request (hard rule #7).
        // Earlier answers were restored for display, so they are redacted again here.
        IPromptRedactionSession redaction;
        try
        {
            redaction = await _anonymizer.BeginSessionAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Could not prepare prompt redaction; no chat request was sent.");
            return new ChatTurn(ErrorMessage, []);
        }

        var systemPrompt = BuildSystemPrompt();
        var messages = new List<AiChatMessage>(history.Count + 1);
        foreach (var turn in history)
        {
            var text = redaction.Redact(turn.Text);
            messages.Add(turn.Author == ChatAuthor.User
                ? AiChatMessage.User(text)
                : AiChatMessage.Assistant(text, []));
        }

        messages.Add(AiChatMessage.User(redaction.Redact(question)));

        var traces = new List<ChatToolTrace>();

        for (var iteration = 0; iteration < MaxIterations; iteration++)
        {
            var request = new AiToolRequest
            {
                Model = AiDefaults.ChatModel,
                Messages = messages,
                SystemPrompt = systemPrompt,
                Tools = _toolDescriptors,
            };

            if (!await CanProceedAsync(systemPrompt, messages, ct).ConfigureAwait(false))
            {
                _logger.LogInformation("Budget gate blocked a chat turn; no API call made.");
                return new ChatTurn(BudgetMessage, [], BudgetExceeded: true);
            }

            AiResult<AiToolTurn> result;
            try
            {
                result = await CallProviderAsync(request, ct).ConfigureAwait(false);
            }
            catch (MissingAiKeyException)
            {
                return new ChatTurn(MissingKeyMessage, [], MissingApiKey: true);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Chat provider call failed after {Attempts} attempt(s).", ProviderAttempts);
                return new ChatTurn(ErrorMessage, traces);
            }

            await _ledger.RecordAsync(result.Usage, AiPurpose.Chat, ct).ConfigureAwait(false);

            var turn = result.Value;
            if (turn.IsFinal)
            {
                // The answer stays on the device, so the payee pseudonyms become names again.
                return new ChatTurn(redaction.Restore(turn.Text ?? string.Empty), traces);
            }

            var toolResults = await ExecuteToolsAsync(turn.ToolCalls, traces, redaction, ct).ConfigureAwait(false);
            messages.Add(AiChatMessage.Assistant(turn.Text, turn.ToolCalls));
            messages.Add(AiChatMessage.ToolOutputs(toolResults));
        }

        _logger.LogWarning("Chat tool-call loop hit the {Max}-iteration cap without a final answer.", MaxIterations);
        return new ChatTurn(IncompleteMessage, traces);
    }

    private async Task<IReadOnlyList<AiToolResult>> ExecuteToolsAsync(
        IReadOnlyList<AiToolCall> calls,
        List<ChatToolTrace> traces,
        IPromptRedactionSession redaction,
        CancellationToken ct)
    {
        var results = new List<AiToolResult>(calls.Count);
        foreach (var call in calls)
        {
            // The model only knows payees by pseudonym; a filter such as merchantPattern="[PARTY_1]"
            // must query the real name. Restoring happens locally and never reaches the provider.
            var arguments = PromptJson.Restore(call.ArgumentsJson, redaction);
            traces.Add(new ChatToolTrace(call.ToolName, arguments));

            string rawResult;
            if (_tools.TryGetValue(call.ToolName, out var tool))
            {
                try
                {
                    rawResult = await tool.ExecuteAsync(arguments, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // Tool errors are fed back to the model so it can self-correct (doc 04), never
                    // swallowed silently. Logged with anonymised context only.
                    _logger.LogWarning(ex, "Chat tool {Tool} threw; returning an error result to the model.", call.ToolName);
                    rawResult = $"{{\"error\":\"Tool '{call.ToolName}' failed.\"}}";
                }
            }
            else
            {
                rawResult = $"{{\"error\":\"Unknown tool '{call.ToolName}'.\"}}";
            }

            // Anonymise before the result returns to the model (hard rule #7), value by value.
            var safeResult = PromptJson.Redact(rawResult, redaction);
            results.Add(new AiToolResult(call.CallId, call.ToolName, safeResult));
        }

        return results;
    }

    private async Task<bool> CanProceedAsync(
        string systemPrompt, IReadOnlyList<AiChatMessage> messages, CancellationToken ct)
    {
        var estimatedInputTokens = EstimateInputTokens(systemPrompt, messages);
        var estimate = _pricing.EstimateBeforeCall(AiDefaults.ChatModel, estimatedInputTokens, EstimatedOutputTokens);
        return await _budgetGate.CanProceedAsync(estimate.Pln, AiPriority.Normal, ct).ConfigureAwait(false);
    }

    private int EstimateInputTokens(string systemPrompt, IReadOnlyList<AiChatMessage> messages)
    {
        var chars = systemPrompt.Length;
        chars += _toolDescriptors.Sum(t => t.Name.Length + t.Description.Length + t.ParametersJsonSchema.Length);
        foreach (var message in messages)
        {
            chars += message.Text?.Length ?? 0;
            chars += message.ToolCalls.Sum(c => c.ToolName.Length + c.ArgumentsJson.Length);
            chars += message.ToolResults.Sum(r => r.ResultJson.Length);
        }

        return Math.Max(1, chars / CharsPerToken);
    }

    private async Task<AiResult<AiToolTurn>> CallProviderAsync(AiToolRequest request, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await _provider.CompleteWithToolsAsync(request, ct).ConfigureAwait(false);
            }
            catch (MissingAiKeyException)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (attempt < ProviderAttempts)
            {
                _logger.LogWarning(ex, "Chat provider call failed (attempt {Attempt}/{Max}); retrying.", attempt, ProviderAttempts);
                await Task.Delay(_retryBackoff, ct).ConfigureAwait(false);
            }
        }
    }

    private static string BuildSystemPrompt()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");
        return
            "Jesteś asystentem finansowym w aplikacji Coffer. Odpowiadasz po polsku, zwięźle (2-4 zdania). "
            + $"Dzisiejsza data: {today}. "
            + "Odpowiadaj wyłącznie na podstawie danych zwróconych przez narzędzia. NIGDY nie zmyślaj liczb "
            + "ani kwot — jeśli narzędzie nie zwróciło danych, powiedz, że ich nie masz. Wszystkie kwoty są "
            + "w PLN. Nie udzielaj porad podatkowych, prawnych ani inwestycyjnych i nie rekomenduj konkretnych "
            + "instrumentów finansowych. Gdy pytanie dotyczy wydatków lub transakcji, użyj odpowiedniego narzędzia. "
            + "Dane osobowe są zastąpione znacznikami w nawiasach kwadratowych (np. [PARTY_1], [OWNER], [ADDRESS]) — "
            + "przepisuj je dokładnie w tej postaci i nie zgaduj, co oznaczają.";
    }
}
