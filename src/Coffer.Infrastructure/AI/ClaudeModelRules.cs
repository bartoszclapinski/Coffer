using System.Text.RegularExpressions;

namespace Coffer.Infrastructure.AI;

/// <summary>
/// Per-model request rules for the Claude Messages API, kept in one place so
/// <see cref="ClaudeProvider"/> never has to special-case model IDs inline.
/// <list type="bullet">
/// <item><b>Sampling.</b> Claude 4.7 and newer (Opus 4.7/4.8, the 5.x family, including
/// <c>claude-haiku-5-5</c> and <c>claude-sonnet-5-5</c>) reject a non-default <c>temperature</c>
/// with HTTP 400, so it is sent only to the older models that accept it.</item>
/// <item><b>Thinking.</b> The 5.x family thinks by default (adaptive), and thinking tokens count
/// against <c>max_tokens</c>. A cap sized for the answer alone can be used up by thinking, which
/// would truncate a JSON answer, so those models get <see cref="ThinkingHeadroomTokens"/> on top.
/// Thinking is not switched off: Anthropic.SDK 5.10.0 cannot express <c>{"type":"disabled"}</c>
/// (its <c>ThinkingType</c> has only <c>enabled</c> / <c>adaptive</c>), and
/// <c>claude-sonnet-5-5</c> rejects <c>disabled</c> anyway. Headroom is free unless used —
/// only generated tokens are billed, and the ledger records the real usage.</item>
/// </list>
/// An ID that does not parse is treated as the newest generation: no temperature (omitting it is
/// always accepted) and thinking headroom (harmless if the model does not think).
/// </summary>
public static partial class ClaudeModelRules
{
    /// <summary>Extra <c>max_tokens</c> given to models that think by default.</summary>
    public const int ThinkingHeadroomTokens = 8192;

    private static readonly Version _firstWithoutSampling = new(4, 7);
    private static readonly Version _firstThinkingByDefault = new(5, 0);

    /// <summary>True when the model accepts a caller-chosen <c>temperature</c>.</summary>
    public static bool AcceptsTemperature(string model) =>
        TryGetVersion(model) is { } version && version < _firstWithoutSampling;

    /// <summary>True when the model runs (adaptive) thinking even if the request does not ask for it.</summary>
    public static bool ThinksByDefault(string model) =>
        TryGetVersion(model) is not { } version || version >= _firstThinkingByDefault;

    /// <summary>The <c>max_tokens</c> to send for an answer of up to <paramref name="answerTokens"/> tokens.</summary>
    public static int MaxTokensFor(string model, int answerTokens) =>
        ThinksByDefault(model) ? answerTokens + ThinkingHeadroomTokens : answerTokens;

    // claude-<family>-<major>[-<minor>][-<yyyymmdd>], e.g. claude-haiku-5-5, claude-sonnet-5,
    // claude-haiku-4-5-20251001. The minor is at most two digits so a date suffix is never read as one.
    private static Version? TryGetVersion(string model)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            return null;
        }

        var match = ModelIdPattern().Match(model.Trim());
        if (!match.Success)
        {
            return null;
        }

        var major = int.Parse(match.Groups["major"].Value, System.Globalization.CultureInfo.InvariantCulture);
        var minor = match.Groups["minor"].Success
            ? int.Parse(match.Groups["minor"].Value, System.Globalization.CultureInfo.InvariantCulture)
            : 0;
        return new Version(major, minor);
    }

    [GeneratedRegex(
        @"^claude-[a-z]+-(?<major>\d{1,2})(?:-(?<minor>\d{1,2}))?(?:-\d{8})?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ModelIdPattern();
}
