namespace Coffer.Core.Ai;

/// <summary>
/// What an AI call is for, which decides the provider + model it runs on (Sprint 31). Each role has
/// its own route in <see cref="IAiSettings"/>, so the owner can keep e.g. categorisation local and
/// analysis on Claude.
/// </summary>
public enum AiRole
{
    /// <summary>Batch transaction categorisation: cheap, high-volume JSON calls.</summary>
    Categorization = 0,

    /// <summary>
    /// Everything that needs reasoning: chat, the advisor report, anomaly commentary, the cash-flow
    /// explanation and AI fallback statement parsing.
    /// </summary>
    Analysis = 1,
}
