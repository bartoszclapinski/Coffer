using CommunityToolkit.Mvvm.ComponentModel;

namespace Coffer.Application.ViewModels.Settings;

/// <summary>
/// One entry of the AI provider pickers in Settings (Sprint 31-D): the provider name a route is saved with
/// (<see cref="Id"/>, e.g. <c>Ollama</c>) and its localized label (e.g. "Ollama (local)"). The label is
/// refreshed by <see cref="SettingsViewModel"/> when the UI language changes, so both role pickers share
/// the same instances.
/// </summary>
public sealed partial class AiProviderOption : ObservableObject
{
    [ObservableProperty]
    private string _displayName;

    public AiProviderOption(string id, string labelKey, string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(labelKey);
        Id = id;
        LabelKey = labelKey;
        _displayName = displayName;
    }

    /// <summary>The provider name stored in the route (<see cref="Core.Ai.AiDefaults.Providers"/>).</summary>
    public string Id { get; }

    /// <summary>The resource key of <see cref="DisplayName"/>.</summary>
    public string LabelKey { get; }

    public override string ToString() => DisplayName;
}
