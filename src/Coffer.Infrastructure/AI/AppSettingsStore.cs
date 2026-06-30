using System.Globalization;
using Coffer.Core.Ai;
using Coffer.Core.Domain;
using Coffer.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Coffer.Infrastructure.AI;

/// <summary>
/// <see cref="IAiSettings"/> backed by the <see cref="AppSetting"/> key/value table in the
/// encrypted DB. Reads fall back to <see cref="AiDefaults"/> until the owner saves a value;
/// writes upsert a single row per key. API keys never pass through here — they live in
/// <c>ISecretStore</c> (hard rule #6/#11).
/// </summary>
public sealed class AppSettingsStore : IAiSettings
{
    private const string MonthlyCapKey = "ai.monthlyCapPln";
    private const string ActiveProviderKey = "ai.activeProvider";
    private const string CategorizationModelKey = "ai.categorizationModel";

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
