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
/// per-domain <c>*Defaults</c> until the owner saves a value; writes upsert a row per key (a route's two
/// keys in one save). API
/// keys never pass through here — they live in <c>ISecretStore</c> (hard rule #6/#11).
/// <para>
/// Per-role AI routes (Sprint 31) are additive keys, no migration: the pre-31 keys
/// <c>ai.activeProvider</c> / <c>ai.categorizationModel</c> keep meaning the
/// <see cref="AiRole.Categorization"/> route, and <c>ai.analysisProvider</c> / <c>ai.analysisModel</c>
/// hold the <see cref="AiRole.Analysis"/> route. Absent keys read as Claude with the role's default model.
/// <c>ai.ollamaEndpoint</c> holds the Ollama endpoint (absent or invalid reads as the local default).
/// </para>
/// </summary>
public sealed class AppSettingsStore : IAiSettings, IPlanningSettings
{
    private const string MonthlyCapKey = "ai.monthlyCapPln";
    private const string CategorizationProviderKey = "ai.activeProvider";
    private const string CategorizationModelKey = "ai.categorizationModel";
    private const string AnalysisProviderKey = "ai.analysisProvider";
    private const string AnalysisModelKey = "ai.analysisModel";
    private const string OllamaEndpointKey = "ai.ollamaEndpoint";
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

    public async Task<AiRoute> GetRouteAsync(AiRole role, CancellationToken ct)
    {
        var providerKey = ProviderKey(role);
        var modelKey = ModelKey(role);

        // One query in one context: the provider and model always come from the same saved route.
        await using var db = await _contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var stored = await db.AppSettings
            .AsNoTracking()
            .Where(s => s.Key == providerKey || s.Key == modelKey)
            .ToDictionaryAsync(s => s.Key, s => s.Value, StringComparer.Ordinal, ct)
            .ConfigureAwait(false);

        var provider = NormalizeProvider(stored.GetValueOrDefault(providerKey));
        var model = stored.GetValueOrDefault(modelKey)?.Trim();

        // For Claude a stored model that is no longer offered (e.g. the retired "gpt-4o-mini") reads as
        // the role's default, so a role never sends Claude a model ID it would reject with a 400.
        // Another provider's models are not a fixed list (Ollama serves whatever the owner pulled), so
        // its stored model is returned as-is; empty means "not chosen yet" and the route is not usable.
        if (provider == AiDefaults.ClaudeProvider)
        {
            model = AiDefaults.ClaudeModels.FirstOrDefault(m => string.Equals(m, model, StringComparison.OrdinalIgnoreCase))
                ?? AiDefaults.DefaultModel(role);
        }

        return new AiRoute(provider, model ?? string.Empty);
    }

    public async Task SetRouteAsync(AiRole role, AiRoute route, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(route);
        ArgumentException.ThrowIfNullOrWhiteSpace(route.Model);

        var provider = AiDefaults.Providers.FirstOrDefault(
                p => string.Equals(p, route.Provider?.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"'{route.Provider}' is not a known AI provider.", nameof(route));

        // Both keys in one SaveChanges, so a concurrent read never sees half of a route change.
        await using var db = await _contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        await UpsertAsync(db, ProviderKey(role), provider, ct).ConfigureAwait(false);
        await UpsertAsync(db, ModelKey(role), route.Model.Trim(), ct).ConfigureAwait(false);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<Uri> GetOllamaEndpointAsync(CancellationToken ct)
    {
        var raw = await GetValueAsync(OllamaEndpointKey, ct).ConfigureAwait(false);
        return OllamaEndpoint.TryParse(raw, out var endpoint) ? endpoint : OllamaEndpoint.Default;
    }

    public Task SetOllamaEndpointAsync(Uri endpoint, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (!OllamaEndpoint.IsValid(endpoint))
        {
            throw new ArgumentException(
                "The Ollama endpoint must be an absolute http or https URL without credentials, query or fragment.",
                nameof(endpoint));
        }

        return SetValueAsync(OllamaEndpointKey, OllamaEndpoint.Format(endpoint), ct);
    }

    // Nothing stored, or the retired "OpenAI" (offered before Sprint 31 but never implemented, so every
    // such call really ran on Claude), reads as Claude. A known provider is returned in its canonical
    // spelling. Any other value is kept as stored: the router then fails closed with
    // UnknownAiProviderException instead of quietly sending the prompt to a remote vendor.
    private static string NormalizeProvider(string? stored)
    {
        var trimmed = stored?.Trim();
        if (string.IsNullOrEmpty(trimmed)
            || string.Equals(trimmed, AiDefaults.RetiredOpenAiProvider, StringComparison.OrdinalIgnoreCase))
        {
            return AiDefaults.ClaudeProvider;
        }

        return AiDefaults.Providers.FirstOrDefault(p => string.Equals(p, trimmed, StringComparison.OrdinalIgnoreCase))
            ?? trimmed;
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
        await UpsertAsync(db, key, value, ct).ConfigureAwait(false);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    // Stages an insert or update of one key; the caller saves, so several keys can share one save.
    private static async Task UpsertAsync(CofferDbContext db, string key, string value, CancellationToken ct)
    {
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
    }
}
