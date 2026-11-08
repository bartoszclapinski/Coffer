using System.Globalization;
using Coffer.Core.Ai;
using Coffer.Core.Domain;
using Coffer.Core.Planning;
using Coffer.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coffer.Infrastructure.AI;

/// <summary>
/// The owner-settings key/value store backed by the <see cref="AppSetting"/> table in the encrypted DB,
/// serving both <see cref="IAiSettings"/> and <see cref="IPlanningSettings"/>. Reads fall back to the
/// per-domain <c>*Defaults</c> until the owner saves a value; writes upsert a single row per key. API
/// keys never pass through here — they live in <c>ISecretStore</c> (hard rule #6/#11).
/// <para>
/// Per-role AI routes (Sprint 31) are additive keys, no migration: the pre-31 keys
/// <c>ai.activeProvider</c> / <c>ai.categorizationModel</c> keep meaning the
/// <see cref="AiRole.Categorization"/> route, and <c>ai.analysisProvider</c> / <c>ai.analysisModel</c>
/// hold the <see cref="AiRole.Analysis"/> route. Absent keys read as Claude with the role's default model.
/// </para>
/// </summary>
public sealed class AppSettingsStore : IAiSettings, IPlanningSettings
{
    private const string MonthlyCapKey = "ai.monthlyCapPln";
    private const string CategorizationProviderKey = "ai.activeProvider";
    private const string CategorizationModelKey = "ai.categorizationModel";
    private const string AnalysisProviderKey = "ai.analysisProvider";
    private const string AnalysisModelKey = "ai.analysisModel";
    private const string AiFallbackParsingEnabledKey = "ai.fallbackParsingEnabled";
    private const string OwnerIdentityNamesKey = "privacy.ownerIdentityNames";
    private const string SafetyFloorKey = "planning.safetyFloorPln";

    private readonly IDbContextFactory<CofferDbContext> _contextFactory;

    public AppSettingsStore(IDbContextFactory<CofferDbContext> contextFactory)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);
        _contextFactory = contextFactory;
    }

    public async Task<decimal> GetMonthlyCapPlnAsync(CancellationToken ct)
    {
        var raw = await GetValueAsync(MonthlyCapKey, ct).ConfigureAwait(false);
        return raw is not null && decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var cap)
            ? cap
            : AiDefaults.MonthlyCapPln;
    }

    public Task SetMonthlyCapPlnAsync(decimal capPln, CancellationToken ct) =>
        SetValueAsync(MonthlyCapKey, capPln.ToString(CultureInfo.InvariantCulture), ct);

    // A stored provider this build does not know (e.g. "OpenAI", offered before Sprint 31) reads as
    // Claude: now that the provider routes calls, a retired value must not break the role.
    public async Task<string> GetProviderAsync(AiRole role, CancellationToken ct)
    {
        var stored = await GetValueAsync(ProviderKey(role), ct).ConfigureAwait(false);
        return AiDefaults.Providers.FirstOrDefault(p => string.Equals(p, stored?.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? AiDefaults.ClaudeProvider;
    }

    public Task SetProviderAsync(AiRole role, string provider, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(provider);
        return SetValueAsync(ProviderKey(role), provider, ct);
    }

    // For Claude a stored model that is no longer offered (e.g. the retired "gpt-4o-mini") reads as the
    // role's default, so a role never sends Claude a model ID it would reject with a 400. Another
    // provider's models are not a fixed list (Ollama serves whatever the owner pulled), so its stored
    // model is returned as-is; empty means "not chosen yet" and the route is not ready.
    public async Task<string> GetModelAsync(AiRole role, CancellationToken ct)
    {
        var provider = await GetProviderAsync(role, ct).ConfigureAwait(false);
        var stored = (await GetValueAsync(ModelKey(role), ct).ConfigureAwait(false))?.Trim();

        if (provider == AiDefaults.ClaudeProvider)
        {
            return AiDefaults.ClaudeModels.FirstOrDefault(m => string.Equals(m, stored, StringComparison.OrdinalIgnoreCase))
                ?? AiDefaults.DefaultModel(role);
        }

        return stored ?? string.Empty;
    }

    public Task SetModelAsync(AiRole role, string model, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(model);
        return SetValueAsync(ModelKey(role), model, ct);
    }

    public async Task<bool> GetAiFallbackParsingEnabledAsync(CancellationToken ct)
    {
        var raw = await GetValueAsync(AiFallbackParsingEnabledKey, ct).ConfigureAwait(false);
        return raw is not null && bool.TryParse(raw, out var enabled)
            ? enabled
            : AiDefaults.AiFallbackParsingEnabled;
    }

    public Task SetAiFallbackParsingEnabledAsync(bool enabled, CancellationToken ct) =>
        SetValueAsync(AiFallbackParsingEnabledKey, enabled ? "true" : "false", ct);

    public async Task<string?> GetOwnerIdentityNamesAsync(CancellationToken ct)
    {
        var raw = await GetValueAsync(OwnerIdentityNamesKey, ct).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(raw) ? null : raw;
    }

    public Task SetOwnerIdentityNamesAsync(string? names, CancellationToken ct) =>
        SetValueAsync(OwnerIdentityNamesKey, names?.Trim() ?? string.Empty, ct);

    public async Task<decimal> GetSafetyFloorPlnAsync(CancellationToken ct)
    {
        var raw = await GetValueAsync(SafetyFloorKey, ct).ConfigureAwait(false);
        return raw is not null && decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var floor)
            ? floor
            : PlanningDefaults.SafetyFloorPln;
    }

    public Task SetSafetyFloorPlnAsync(decimal floorPln, CancellationToken ct) =>
        SetValueAsync(SafetyFloorKey, floorPln.ToString(CultureInfo.InvariantCulture), ct);

    private static string ProviderKey(AiRole role) => role switch
    {
        AiRole.Categorization => CategorizationProviderKey,
        AiRole.Analysis => AnalysisProviderKey,
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown AI role."),
    };

    private static string ModelKey(AiRole role) => role switch
    {
        AiRole.Categorization => CategorizationModelKey,
        AiRole.Analysis => AnalysisModelKey,
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown AI role."),
    };

    private async Task<string?> GetValueAsync(string key, CancellationToken ct)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var setting = await db.AppSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Key == key, ct)
            .ConfigureAwait(false);
        return setting?.Value;
    }

    private async Task SetValueAsync(string key, string value, CancellationToken ct)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var setting = await db.AppSettings
            .FirstOrDefaultAsync(s => s.Key == key, ct)
            .ConfigureAwait(false);

        if (setting is null)
        {
            db.AppSettings.Add(new AppSetting { Key = key, Value = value });
        }
        else
        {
            setting.Value = value;
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
