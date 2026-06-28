using Coffer.Core.Localization;

namespace Coffer.Application.ViewModels.Settings;

/// <summary>
/// One selectable UI language for the Settings combo box. The display name is shown in
/// the language's own tongue ("Polski", "English") regardless of the active UI language,
/// matching the usual language-picker convention.
/// </summary>
public sealed record LanguageOption(AppLanguage Language, string DisplayName);
