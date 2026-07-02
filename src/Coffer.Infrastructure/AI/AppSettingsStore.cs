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
/// </summary>
public sealed class AppSettingsStore : IAiSettings, IPlanningSettings
{
    private const string MonthlyCapKey = "ai.monthlyCapPln";
    private const string ActiveProviderKey = "ai.activeProvider";
    private const string CategorizationModelKey = "ai.categorizationModel";
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

    public async Task<string> GetActiveProviderAsync(CancellationToken ct) =>
        await GetValueAsync(ActiveProviderKey, ct).ConfigureAwait(false) ?? AiDefaults.ClaudeProvider;

    public Task SetActiveProviderAsync(string provider, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(provider);
        return SetValueAsync(ActiveProviderKey, provider, ct);
    }

    public async Task<string> GetCategorizationModelAsync(CancellationToken ct) =>
        await GetValueAsync(CategorizationModelKey, ct).ConfigureAwait(false) ?? AiDefaults.CategorizationModel;

    public Task SetCategorizationModelAsync(string model, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(model);
        return SetValueAsync(CategorizationModelKey, model, ct);
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
