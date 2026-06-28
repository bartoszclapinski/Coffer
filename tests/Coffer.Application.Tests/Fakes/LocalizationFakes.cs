using Coffer.Application.Localization;
using Coffer.Core.Localization;

namespace Coffer.Application.Tests.Fakes;

/// <summary>
/// In-memory <see cref="ILocalizer"/> for view-model tests. The indexer echoes the key so
/// assertions can check which key a view-model resolved; <see cref="SetLanguage"/> records
/// the switch and raises <see cref="LanguageChanged"/>.
/// </summary>
internal sealed class FakeLocalizer : ILocalizer
{
    public AppLanguage Current { get; private set; } = AppLanguage.Polish;

    public int SetLanguageCalls { get; private set; }

    public string this[string key] => key;

    public string Format(string key, params object[] args) => key;

    public void SetLanguage(AppLanguage language)
    {
        SetLanguageCalls++;
        if (language == Current)
        {
            return;
        }

        Current = language;
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? LanguageChanged;
}

/// <summary>In-memory <see cref="ILanguageStore"/> recording the last saved language.</summary>
internal sealed class FakeLanguageStore : ILanguageStore
{
    public AppLanguage Stored { get; set; } = AppLanguage.Polish;

    public int SaveCalls { get; private set; }

    public AppLanguage Load() => Stored;

    public void Save(AppLanguage language)
    {
        SaveCalls++;
        Stored = language;
    }
}
