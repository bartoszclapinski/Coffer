namespace Coffer.Core.Localization;

/// <summary>
/// Persists the owner's chosen UI language. Stored as a small plaintext file (not the
/// encrypted DB) because the choice is non-sensitive and must be readable on the
/// pre-login screens (setup wizard, login) before the DEK exists. Reads default to
/// <see cref="AppLanguage.Polish"/> when no value has been saved.
/// </summary>
public interface ILanguageStore
{
    AppLanguage Load();

    void Save(AppLanguage language);
}
