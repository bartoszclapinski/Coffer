using Coffer.Core.Localization;

namespace Coffer.Application.Localization;

/// <summary>
/// Runtime UI string lookup. A singleton: every view binds to the same instance through
/// the Avalonia <c>{l:Localize Key}</c> markup extension, so a single
/// <see cref="SetLanguage"/> call re-labels the whole UI live (no restart). Lives in
/// <c>Coffer.Application</c> (no UI dependency) so the mobile app can reuse it.
/// </summary>
public interface ILocalizer
{
    AppLanguage Current { get; }

    /// <summary>The localized string for <paramref name="key"/>, or the key itself if missing.</summary>
    string this[string key] { get; }

    /// <summary><see cref="string.Format(IFormatProvider, string, object[])"/> over the localized template.</summary>
    string Format(string key, params object[] args);

    void SetLanguage(AppLanguage language);

    /// <summary>Raised after the active language changes so listeners can refresh.</summary>
    event EventHandler? LanguageChanged;
}
