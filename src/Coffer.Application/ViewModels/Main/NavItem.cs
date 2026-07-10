using Coffer.Application.Localization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Coffer.Application.ViewModels.Main;

/// <summary>
/// One entry in the shell's navigation model — the single source that drives both the icon
/// rail and the command palette. Replaces the former per-section bool flags + <c>Show*</c>
/// commands. <see cref="Title"/> is resolved live through <see cref="ILocalizer"/> so it
/// follows a runtime language change (the shell refreshes it on <c>LanguageChanged</c>).
/// </summary>
public sealed partial class NavItem : ObservableObject
{
    private readonly ILocalizer _localizer;
    private readonly Action _load;

    public NavItem(string key, string titleKey, string icon, ObservableObject page, Action load, ILocalizer localizer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(titleKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(icon);
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(load);
        ArgumentNullException.ThrowIfNull(localizer);

        Key = key;
        TitleKey = titleKey;
        Icon = icon;
        Page = page;
        _load = load;
        _localizer = localizer;
    }

    /// <summary>Stable identifier (used by the command palette / navigation by key).</summary>
    public string Key { get; }

    /// <summary>Localization key for the screen title.</summary>
    public string TitleKey { get; }

    /// <summary>Phosphor icon name (resolved to a glyph by the view).</summary>
    public string Icon { get; }

    /// <summary>The section view-model shown when this item is active.</summary>
    public ObservableObject Page { get; }

    /// <summary>Live-localized display title (top bar + rail tooltip + palette).</summary>
    public string Title => _localizer[TitleKey];

    [ObservableProperty]
    private bool _isActive;

    /// <summary>Runs the section's load action (query refresh) on navigation.</summary>
    public void Load() => _load();

    /// <summary>Re-raises <see cref="Title"/> after a language change.</summary>
    public void RefreshTitle() => OnPropertyChanged(nameof(Title));
}
