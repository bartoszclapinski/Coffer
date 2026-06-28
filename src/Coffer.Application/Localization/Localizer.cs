using System.Globalization;
using System.Resources;
using Coffer.Core.Localization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Coffer.Application.Localization;

/// <summary>
/// <see cref="ILocalizer"/> backed by a <see cref="ResourceManager"/> over
/// <c>Strings.resx</c> (English neutral) + <c>Strings.pl.resx</c> (Polish satellite).
/// Implements <see cref="INotifyPropertyChanged"/> on the indexer so the Avalonia markup
/// extension's bindings re-evaluate when the language changes — the entire UI updates
/// without a restart.
/// </summary>
public sealed class Localizer : ObservableObject, ILocalizer
{
    private static readonly ResourceManager _resources =
        new("Coffer.Application.Localization.Strings", typeof(Localizer).Assembly);

    private static readonly CultureInfo _polish = CultureInfo.GetCultureInfo("pl");
    private static readonly CultureInfo _english = CultureInfo.GetCultureInfo("en");

    private AppLanguage _current = AppLanguage.Polish;
    private CultureInfo _culture = _polish;

    public AppLanguage Current => _current;

    public event EventHandler? LanguageChanged;

    public string this[string key] => _resources.GetString(key, _culture) ?? key;

    public string Format(string key, params object[] args) =>
        string.Format(_culture, this[key], args);

    public void SetLanguage(AppLanguage language)
    {
        if (language == _current)
        {
            return;
        }

        _current = language;
        _culture = language == AppLanguage.Polish ? _polish : _english;
        CultureInfo.CurrentUICulture = _culture;
        CultureInfo.DefaultThreadCurrentUICulture = _culture;

        // "Item[]" is the conventional indexer change notification — it invalidates every
        // binding to this[key] at once, so all localized controls refresh together.
        OnPropertyChanged("Item[]");
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }
}
