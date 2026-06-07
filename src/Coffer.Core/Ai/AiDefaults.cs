namespace Coffer.Core.Ai;

/// <summary>Shared AI constants: provider labels, default model/cap, secret and purpose keys.</summary>
public static class AiDefaults
{
    public const string ClaudeProvider = "Claude";
    public const string OpenAiProvider = "OpenAI";

    /// <summary>Cheap batch categorisation model (doc 04 / CLAUDE.md cost discipline).</summary>
    public const string CategorizationModel = "claude-haiku-4-5";

    /// <summary>
    /// Reasoning-tier model for chat (doc 04 / CLAUDE.md cost discipline): chat needs reasoning,
    /// not the cheap categorisation model. User-selectable chat-model settings arrive with the
    /// Settings UI in 12-B; until then chat uses this default.
    /// </summary>
    public const string ChatModel = "claude-sonnet-4-6";

    /// <summary>Default monthly budget cap in PLN (doc 04).</summary>
    public const decimal MonthlyCapPln = 20m;

    /// <summary><c>ISecretStore</c> key for the Claude API key.</summary>
    public const string ClaudeApiKeySecret = "ai.claude.apiKey";

    /// <summary><c>ISecretStore</c> key for the OpenAI API key.</summary>
    public const string OpenAiApiKeySecret = "ai.openai.apiKey";
}

/// <summary>Ledger <c>Purpose</c> values (doc 04).</summary>
public static class AiPurpose
{
    public const string Categorization = "categorization";
    public const string Chat = "chat";
    public const string Vision = "vision";
    public const string ParserFallback = "parser-fallback";
    public const string AnomalyComment = "anomaly-comment";
}
