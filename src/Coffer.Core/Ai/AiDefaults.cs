namespace Coffer.Core.Ai;

/// <summary>
/// Shared AI constants: provider labels, default models/cap, secret and purpose keys. The default
/// models are settings defaults only — AI consumers never read them directly; they resolve their
/// role's route through <see cref="IAiRouteResolver"/>.
/// </summary>
public static class AiDefaults
{
    /// <summary>The only wired remote provider (doc 04 lists the deferred ones).</summary>
    public const string ClaudeProvider = "Claude";

    /// <summary>
    /// The local Ollama provider. Calls cost nothing. Its provider implementation arrives in Sprint 31-C;
    /// until it is registered, a route naming it fails as an unknown provider.
    /// </summary>
    public const string OllamaProvider = "Ollama";

    /// <summary>The provider names a route may be saved with.</summary>
    public static IReadOnlyList<string> Providers { get; } = [ClaudeProvider, OllamaProvider];

    /// <summary>
    /// The one retired provider name a stored route may still hold (offered in Settings before
    /// Sprint 31, never implemented); it reads as <see cref="ClaudeProvider"/>, which every pre-31
    /// call actually used. Any other unknown name is kept and fails closed at the router.
    /// </summary>
    public const string RetiredOpenAiProvider = "OpenAI";

    /// <summary>Default <see cref="AiRole.Categorization"/> model: cheap batch categorisation (doc 04 / CLAUDE.md cost discipline).</summary>
    public const string CategorizationModel = "claude-haiku-5-5";

    /// <summary>
    /// Default <see cref="AiRole.Analysis"/> model: chat, advisor report, anomaly commentary, cash-flow
    /// explanation and AI fallback parsing need reasoning, not the cheap categorisation model
    /// (doc 04 / CLAUDE.md cost discipline).
    /// </summary>
    public const string AnalysisModel = "claude-sonnet-5-5";

    /// <summary>The Claude model a role uses until the owner picks another.</summary>
    public static string DefaultModel(AiRole role) =>
        role == AiRole.Categorization ? CategorizationModel : AnalysisModel;

    /// <summary>
    /// The Claude models the owner can pick: current generation first, the 4.x models kept as a
    /// fallback. A stored model outside this list (e.g. a retired OpenAI model) reads as the default.
    /// </summary>
    public static IReadOnlyList<string> ClaudeModels { get; } =
        ["claude-haiku-5-5", "claude-sonnet-5-5", "claude-haiku-4-5", "claude-sonnet-4-6"];

    /// <summary>Default monthly budget cap in PLN (doc 04).</summary>
    public const decimal MonthlyCapPln = 20m;

    /// <summary>
    /// AI-assisted fallback parsing is off until the owner opts in — it sends statement text to
    /// the AI provider (the most data-exposing AI feature), so it needs explicit consent.
    /// </summary>
    public const bool AiFallbackParsingEnabled = false;

    /// <summary><c>ISecretStore</c> key for the Claude API key.</summary>
    public const string ClaudeApiKeySecret = "ai.claude.apiKey";
}

/// <summary>Ledger <c>Purpose</c> values (doc 04).</summary>
public static class AiPurpose
{
    public const string Categorization = "categorization";
    public const string Chat = "chat";
    public const string Vision = "vision";
    public const string ParserFallback = "parser-fallback";
    public const string AnomalyComment = "anomaly-comment";
    public const string AdvisorReport = "advisor-report";
    public const string CashFlowExplain = "cashflow-explain";
}
